﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Microsoft.Common.Core;
using Microsoft.Common.Core.Disposables;
using Microsoft.Common.Core.Shell;
using Microsoft.Common.Core.Tasks;
using Microsoft.Common.Core.Threading;
using Microsoft.R.Host.Client.Host;
using static System.FormattableString;

namespace Microsoft.R.Host.Client.Session {
    internal sealed class RSession : IRSession, IRCallbacks {
        private static readonly string RemotePromptPrefix = "\u26b9";
        private static readonly string DefaultPrompt = "> ";

        private static readonly Task<IRSessionInteraction> CanceledBeginInteractionTask;

        private readonly BufferBlock<RSessionRequestSource> _pendingRequestSources = new BufferBlock<RSessionRequestSource>();

        public event EventHandler<RBeforeRequestEventArgs> BeforeRequest;
        public event EventHandler<RAfterRequestEventArgs> AfterRequest;
        public event EventHandler<EventArgs> Mutated;
        public event EventHandler<ROutputEventArgs> Output;
        public event EventHandler<RConnectedEventArgs> Connected;
        public event EventHandler<EventArgs> Interactive;
        public event EventHandler<EventArgs> Disconnected;
        public event EventHandler<EventArgs> Disposed;
        public event EventHandler<EventArgs> DirectoryChanged;
        public event EventHandler<EventArgs> PackagesInstalled;
        public event EventHandler<EventArgs> PackagesRemoved;

        /// <summary>
        /// ReadConsole requires a task even if there are no pending requests
        /// </summary>
        private IReadOnlyList<IRContext> _contexts;
        private RHost _host;
        private Task _hostRunTask;
        private TaskCompletionSourceEx<object> _hostStartedTcs;
        private RSessionRequestSource _currentRequestSource;
        private TaskCompletionSourceEx<object> _initializedTcs;
        private bool _processingChangeDirectoryCommand;
        private readonly Action _onDispose;
        private readonly IExclusiveReaderLock _initializationLock;
        private readonly BinaryAsyncLock _stopHostLock;
        private readonly CountdownDisposable _disableMutatingOnReadConsole;
        private readonly DisposeToken _disposeToken;
        private volatile bool _isHostRunning;
        private volatile bool _delayedMutatedOnReadConsole;
        private volatile IRSessionCallback _callback;
        private volatile RHostStartupInfo _startupInfo;

        public int Id { get; }
        public string Prompt { get; private set; } = DefaultPrompt;
        public int MaxLength { get; private set; } = 0x1000;
        public bool IsHostRunning => _isHostRunning;
        public Task HostStarted => _hostStartedTcs.Task;
        public bool IsRemote => BrokerClient.IsRemote;
        public bool RestartOnBrokerSwitch { get; set; }

        internal IBrokerClient BrokerClient { get; }
        internal bool IsDisposed => _disposeToken.IsDisposed;

        /// <summary>
        /// For testing purpose only
        /// Do not expose this property to the IRSession interface
        /// </summary> 
        internal RHost RHost => _host;

        static RSession() {
            CanceledBeginInteractionTask = TaskUtilities.CreateCanceled<IRSessionInteraction>(new RHostDisconnectedException());
        }

        public RSession(int id, IBrokerClient brokerClient, IExclusiveReaderLock initializationLock, Action onDispose) {
            Id = id;
            BrokerClient = brokerClient;
            _onDispose = onDispose;
            _disposeToken = DisposeToken.Create<RSession>();
            _disableMutatingOnReadConsole = new CountdownDisposable(() => {
                if (!_delayedMutatedOnReadConsole) {
                    return;
                }

                _delayedMutatedOnReadConsole = false;
                Task.Run(() => Mutated?.Invoke(this, EventArgs.Empty));
            });

            _initializationLock = initializationLock;
            _stopHostLock = new BinaryAsyncLock(true);
            _hostStartedTcs = new TaskCompletionSourceEx<object>();
        }

        private string GetDefaultPrompt(string requestedPrompt = null) {
            if (string.IsNullOrEmpty(requestedPrompt)) {
                requestedPrompt = DefaultPrompt;
            }
            return IsRemote ? RemotePromptPrefix + requestedPrompt : requestedPrompt;
        }

        private void OnMutated() {
            if (_disableMutatingOnReadConsole.Count == 0) {
                Mutated?.Invoke(this, EventArgs.Empty);
            } else {
                _delayedMutatedOnReadConsole = true;
            }
        }

        public void Dispose() {
            if (!_disposeToken.TryMarkDisposed()) {
                return;
            }

            _host?.Dispose();
            Disposed?.Invoke(this, EventArgs.Empty);
            _onDispose();
        }

        public Task<IRSessionInteraction> BeginInteractionAsync(bool isVisible = true, CancellationToken cancellationToken = default(CancellationToken)) {
            _disposeToken.ThrowIfDisposed();

            if (!_isHostRunning) {
                return CanceledBeginInteractionTask;
            }

            RSessionRequestSource requestSource = new RSessionRequestSource(isVisible, cancellationToken);
            _pendingRequestSources.Post(requestSource);

            return _isHostRunning ? requestSource.CreateRequestTask : CanceledBeginInteractionTask;
        }
        
        public Task<REvaluationResult> EvaluateAsync(string expression, REvaluationKind kind = REvaluationKind.Normal, CancellationToken cancellationToken = default(CancellationToken)) {
            return EvaluateAsync(expression, kind, true, cancellationToken);
        }

        private async Task<REvaluationResult> EvaluateAsync(string expression, REvaluationKind kind, bool waitUntilInitialized, CancellationToken cancellationToken = default(CancellationToken)) {
            if (!IsHostRunning) {
                throw new RHostDisconnectedException();
            }

            if (waitUntilInitialized) {
                await _initializedTcs.Task;
            }

            try {
                var result = await _host.EvaluateAsync(expression, kind, cancellationToken);
                if (kind.HasFlag(REvaluationKind.Mutating)) {
                    OnMutated();
                }
                return result;
            } catch (MessageTransportException) when (!IsHostRunning) {
                throw new RHostDisconnectedException();
            }
        }

        public Task<ulong> CreateBlobAsync(CancellationToken ct = default(CancellationToken)) =>
            DoBlobServiceAsync(_host?.CreateBlobAsync(ct));

        public Task DestroyBlobsAsync(IEnumerable<ulong> blobIds, CancellationToken ct = default(CancellationToken)) =>
            DoBlobServiceAsync(new Lazy<Task<long>>(async () => {
                var task = _host?.DestroyBlobsAsync(blobIds, ct) ?? Task.CompletedTask;
                await task;
                return 0;
            }).Value);

        public Task<byte[]> BlobReadAllAsync(ulong blobId, CancellationToken ct = default(CancellationToken)) =>
            DoBlobServiceAsync(_host?.BlobReadAllAsync(blobId, ct));

        public Task<byte[]> BlobReadAsync(ulong blobId, long position, long count, CancellationToken ct = default(CancellationToken)) =>
            DoBlobServiceAsync(_host?.BlobReadAsync(blobId, position, count, ct));

        public Task<long> BlobWriteAsync(ulong blobId, byte[] data, long position, CancellationToken ct = default(CancellationToken)) =>
            DoBlobServiceAsync(_host?.BlobWriteAsync(blobId, data, position, ct));

        public Task<long> GetBlobSizeAsync(ulong blobId, CancellationToken ct = default(CancellationToken)) =>
            DoBlobServiceAsync(_host?.GetBlobSizeAsync(blobId, ct));

        public Task<long> SetBlobSizeAsync(ulong blobId, long size, CancellationToken ct = default(CancellationToken)) =>
            DoBlobServiceAsync(_host?.SetBlobSizeAsync(blobId, size, ct));

        private async Task<T> DoBlobServiceAsync<T>(Task<T> work) {
            if (!IsHostRunning) {
                throw new RHostDisconnectedException();
            }

            await _initializedTcs.Task;

            try {
                return await work;
            } catch (MessageTransportException) when (!IsHostRunning) {
                throw new RHostDisconnectedException();
            }
        }

        public async Task CancelAllAsync(CancellationToken cancellationToken = default(CancellationToken)) {
            using (_disposeToken.Link(ref cancellationToken)) {
                var exception = new OperationCanceledException();
                ClearPendingRequests(exception);
                var currentRequest = Interlocked.Exchange(ref _currentRequestSource, null);

                try {
                    await _host.CancelAllAsync(cancellationToken);
                } finally {
                    // Even if cancellationToken.IsCancellationRequested == true, we can't find out if request was fully completed or not, so we consider it canceled
                    currentRequest?.TryCancel(exception);
                }
            }
        }

        public Task EnsureHostStartedAsync(RHostStartupInfo startupInfo, IRSessionCallback callback, int timeout = 3000, CancellationToken cancellationToken = default(CancellationToken))
            => StartHostAsync(startupInfo, callback, timeout, false, cancellationToken);

        public Task StartHostAsync(RHostStartupInfo startupInfo, IRSessionCallback callback, int timeout = 3000, CancellationToken cancellationToken = default(CancellationToken))
            => StartHostAsync(startupInfo, callback, timeout, true, cancellationToken);

        private async Task StartHostAsync(RHostStartupInfo startupInfo, IRSessionCallback callback, int timeout, bool throwIfStarted, CancellationToken cancellationToken) {
            using (_disposeToken.Link(ref cancellationToken)) {
                await TaskUtilities.SwitchToBackgroundThread();

                using (await _initializationLock.WaitAsync(cancellationToken)) {
                    if (_hostStartedTcs.Task.Status != TaskStatus.RanToCompletion || !_isHostRunning) {
                        await StartHostAsyncBackground(startupInfo, callback, timeout, cancellationToken);
                    } else if (throwIfStarted) {
                        throw new InvalidOperationException("Another instance of RHost is running for this RSession. Stop it before starting new one.");
                    }
                }
            }
        }

        private async Task StartHostAsyncBackground(RHostStartupInfo startupInfo, IRSessionCallback callback, int timeout, CancellationToken cancellationToken) {
            TaskUtilities.AssertIsOnBackgroundThread();

            _callback = callback;
            _startupInfo = startupInfo;
            RHost host;
            try {
                var connectionInfo = new BrokerConnectionInfo(startupInfo.Name, this, startupInfo.RHostCommandLineArguments, timeout);
                host = await BrokerClient.ConnectAsync(connectionInfo, cancellationToken);
            } catch (OperationCanceledException ex) {
                _hostStartedTcs.TrySetCanceled(ex);
                throw;
            } catch (Exception ex) {
                _hostStartedTcs.TrySetException(ex);
                throw;
            }

            await StartHostAsyncBackground(host, cancellationToken);
        }

        private async Task StartHostAsyncBackground(RHost host, CancellationToken cancellationToken = default(CancellationToken)) {
            TaskUtilities.AssertIsOnBackgroundThread();

            ResetInitializationTcs();
            ClearPendingRequests(new RHostDisconnectedException());

            Interlocked.Exchange(ref _host, host);
            Interlocked.Exchange(ref _initializedTcs, new TaskCompletionSourceEx<object>());

            var initializationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var hostRunTask = RunHost(initializationCts.Token);
            Interlocked.Exchange(ref _hostRunTask, hostRunTask)?.DoNotWait();

            await _hostStartedTcs.Task;
            await _initializedTcs.Task;

            initializationCts.Dispose();
            _stopHostLock.EnqueueReset();
        }

        public IRSessionSwitchBrokerTransaction StartSwitchingBroker() =>
            !_disposeToken.IsDisposed && _startupInfo != null && RestartOnBrokerSwitch ? new BrokerTransaction(this) : null;

        public async Task ReconnectAsync(CancellationToken cancellationToken) {
            if (_startupInfo == null) {
                return;
            }

            using (_disposeToken.Link(ref cancellationToken)) {
                var host = _host;
                // host may be null if previous attempts to start it have failed
                if (host != null) {
                    // Detach RHost from RSession
                    host.DetachCallback();

                    // Cancel all current requests (if any)
                    await CancelAllAsync(cancellationToken);

                    host.Dispose();
                    await _hostRunTask;
                }

                var connectionInfo = new BrokerConnectionInfo(_startupInfo.Name, this, _startupInfo.RHostCommandLineArguments);
                host = await BrokerClient.ConnectAsync(connectionInfo, cancellationToken);

                await StartHostAsyncBackground(host, cancellationToken);
            }
        }

        private int _stopCount;
        public async Task StopHostAsync(CancellationToken cancellationToken = default(CancellationToken)) {
            using (_disposeToken.Link(ref cancellationToken)) {
                await TaskUtilities.SwitchToBackgroundThread();

                Interlocked.Increment(ref _stopCount);
                var stopToken = await _stopHostLock.WaitAsync(cancellationToken);
                if (stopToken.IsSet) {
                    return;
                }

                try {
                    ResetInitializationTcs();
                    await StopHostAsync(BrokerClient, _startupInfo.Name, _host, _hostRunTask);

                    stopToken.Set();
                } finally {
                    Interlocked.Decrement(ref _stopCount);
                    stopToken.Reset();
                }
            }
        }

        private static async Task StopHostAsync(IBrokerClient brokerClient, string hostName, RHost host, Task hostRunTask) {
            // Try graceful shutdown with q() first.
            if (host != null) {
                try {
                    host.QuitAsync().SilenceException<Exception>().DoNotWait();
                    await Task.WhenAny(hostRunTask, Task.Delay(10000)).Unwrap();
                } catch (Exception) { }

                if (hostRunTask.IsCompleted) {
                    return;
                }
            }

            // If it didn't work, tell the broker to forcibly terminate the host process. 
            if (hostName != null) {
                try {
                    brokerClient.TerminateSessionAsync(hostName).Wait(10000);
                } catch (Exception) { }

                if (hostRunTask.IsCompleted) {
                    return;
                }
            }

            if (host != null) {
                // If nothing worked, then just disconnect.
                await host.DisconnectAsync();
            }

            await hostRunTask;
        }

        public IDisposable DisableMutatedOnReadConsole() {
            return _disableMutatingOnReadConsole.Increment();
        }

        private async Task RunHost(CancellationToken initializationCt) {
            try {
                await _host.Run(initializationCt);
            } catch (OperationCanceledException oce) {
                _hostStartedTcs.TrySetCanceled(oce);
            } catch (MessageTransportException mte) {
                _hostStartedTcs.TrySetCanceled(new RHostDisconnectedException(string.Empty, mte));
            } catch (Exception ex) {
                _hostStartedTcs.TrySetException(ex);
            }
        }

        private void ResetInitializationTcs() {
            while (true) {
                var tcs = _hostStartedTcs;
                if (!tcs.Task.IsCompleted) {
                    return;
                }

                if (Interlocked.CompareExchange(ref _hostStartedTcs, new TaskCompletionSourceEx<object>(), tcs) == tcs) {
                    return;
                }
            }
        }

        private async Task AfterHostStarted(RHostStartupInfo startupInfo) {
            var evaluator = new BeforeInitializedRExpressionEvaluator(this);
            try {
                // Load RTVS R package before doing anything in R since the calls
                // below calls may depend on functions exposed from the RTVS package
                var libPath = IsRemote ? "." : Path.GetDirectoryName(Assembly.GetExecutingAssembly().GetAssemblyPath());

                await LoadRtvsPackage(evaluator, libPath);

                var wd = startupInfo.WorkingDirectory;
                if (!IsRemote && !string.IsNullOrEmpty(wd) && wd.HasReadPermissions()) {
                    try {
                        await evaluator.SetWorkingDirectoryAsync(wd);
                    } catch(REvaluationException) {
                        await evaluator.SetDefaultWorkingDirectoryAsync();
                    }
                } else {
                    await evaluator.SetDefaultWorkingDirectoryAsync();
                }

                var callback = _callback;
                if (callback != null) {
                    await evaluator.SetVsGraphicsDeviceAsync();

                    string mirrorUrl = callback.CranUrlFromName(startupInfo.CranMirrorName);

                    try {
                        await evaluator.SetVsCranSelectionAsync(mirrorUrl);
                    } catch (REvaluationException ex) {
                        await WriteErrorAsync(Resources.Error_SessionInitializationMirror, mirrorUrl, ex.Message);
                    }

                    try {
                        await evaluator.SetCodePageAsync(startupInfo.CodePage);
                    } catch (REvaluationException ex) {
                        await WriteErrorAsync(Resources.Error_SessionInitializationCodePage, startupInfo.CodePage, ex.Message);
                    }

                    try {
                        await evaluator.SetROptionsAsync();
                    } catch (REvaluationException ex) {
                        await WriteErrorAsync(Resources.Error_SessionInitializationOptions, ex.Message);
                    }

                    await evaluator.OverrideFunctionAsync("setwd", "base");
                    await evaluator.SetFunctionRedirectionAsync();

                    try {
                        await evaluator.OptionsSetWidthAsync(startupInfo.TerminalWidth);
                    } catch (REvaluationException ex) {
                        await WriteErrorAsync(Resources.Error_SessionInitializationOptions, ex.Message);
                    }

                    if (startupInfo.EnableAutosave) {
                        try {
                            // Only enable autosave for this session after querying the user about any existing file.
                            // This way, if they happen to disconnect while still querying, we don't save the new empty
                            // session and overwrite the old file.
                            bool deleteExisting = await evaluator.QueryReloadAutosaveAsync();
                            await evaluator.EnableAutosaveAsync(deleteExisting);
                        } catch (REvaluationException ex) {
                            await WriteErrorAsync(Resources.Error_SessionInitializationAutosave, ex.Message);
                        }
                    }

                    Interactive?.Invoke(this, EventArgs.Empty);
                }

                _initializedTcs.SetResult(null);
            } catch (Exception ex) when (!ex.IsCriticalException()) {
                await WriteErrorAsync(Resources.Error_SessionInitialization, ex);
                if (!(ex is RHostDisconnectedException)) {
                    StopHostAsync().DoNotWait();
                }

                var oce = ex as OperationCanceledException;
                if (oce != null) {
                    _initializedTcs.SetCanceled(oce);
                } else {
                    _initializedTcs.SetException(ex);
                }
            }
        }

        private const int rtvsPackageVersion = 1;

        private static Task LoadRtvsPackage(IRExpressionEvaluator eval, string libPath) {
            return eval.ExecuteAsync(Invariant($@"
if (!base::isNamespaceLoaded('rtvs')) {{
    base::loadNamespace('rtvs', lib.loc = {libPath.ToRStringLiteral()})
}}
if (rtvs:::version != {rtvsPackageVersion}) {{
    warning('This R session was created using an incompatible version of RTVS, and may misbehave or crash when used with this version. Click ""Reset"" to replace it with a new clean session.');
}}
"));
        }

        public void FlushLog() {
            _host?.FlushLog();
        }

        Task IRCallbacks.Connected(string rVersion) {
            Prompt = GetDefaultPrompt();
            _isHostRunning = true;
            _hostStartedTcs.SetResult(null);
            Connected?.Invoke(this, new RConnectedEventArgs(rVersion));
            Mutated?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        Task IRCallbacks.Disconnected() {
            _isHostRunning = false;
            Disconnected?.Invoke(this, EventArgs.Empty);

            var currentRequest = Interlocked.Exchange(ref _currentRequestSource, null);
            var exception = new RHostDisconnectedException();
            currentRequest?.TryCancel(exception);

            ClearPendingRequests(exception);
            return Task.CompletedTask;
        }

        Task IRCallbacks.Shutdown(bool rDataSaved) {
            return Task.CompletedTask;
        }

        private void ClearPendingRequests(OperationCanceledException exception) {
            RSessionRequestSource requestSource;
            while (_pendingRequestSources.TryReceive(out requestSource)) {
                requestSource.TryCancel(exception);
            }

            _contexts = null;
            Prompt = GetDefaultPrompt();
        }


        async Task<string> IRCallbacks.ReadConsole(IReadOnlyList<IRContext> contexts, string prompt, int len, bool addToHistory, CancellationToken ct) {
            await TaskUtilities.SwitchToBackgroundThread();

            if (!_initializedTcs.Task.IsCompleted) {
                await AfterHostStarted(_startupInfo);
            }

            var callback = _callback;
            if (!addToHistory && callback != null) {
                return await callback.ReadUserInput(prompt, len, ct);
            }

            var currentRequest = Interlocked.Exchange(ref _currentRequestSource, null);

            _contexts = contexts;
            Prompt = GetDefaultPrompt(prompt);
            MaxLength = len;

            var requestEventArgs = new RBeforeRequestEventArgs(contexts, Prompt, len, addToHistory);
            BeforeRequest?.Invoke(this, requestEventArgs);

            OnMutated();

            currentRequest?.CompleteResponse();

            string consoleInput = null;
            do {
                ct.ThrowIfCancellationRequested();
                try {
                    consoleInput = await ReadNextRequest(Prompt, len, ct);
                } catch (OperationCanceledException) {
                    // If request was canceled through means other than our token, it indicates the refusal of
                    // that requestor to respond to that particular prompt, so move on to the next requestor.
                    // If it was canceled through the token, then host itself is shutting down, and cancellation
                    // will be propagated on the entry to next iteration of this loop.
                }
            } while (consoleInput == null);

            
            // We only want to fire 'directory changed' events when it is initiated by the user
            _processingChangeDirectoryCommand = consoleInput.StartsWithOrdinal("setwd");

            consoleInput = consoleInput.EnsureLineBreak();
            AfterRequest?.Invoke(this, new RAfterRequestEventArgs(contexts, Prompt, consoleInput, addToHistory, currentRequest?.IsVisible ?? false));

            return consoleInput;
        }

        private async Task<string> ReadNextRequest(string prompt, int len, CancellationToken ct) {
            TaskUtilities.AssertIsOnBackgroundThread();

            var requestSource = await _pendingRequestSources.ReceiveAsync(ct);
            TaskCompletionSource<string> requestTcs = new TaskCompletionSource<string>();
            Interlocked.Exchange(ref _currentRequestSource, requestSource);

            requestSource.Request(_contexts, prompt, len, requestTcs);
            using (ct.Register(() => requestTcs.TrySetCanceled())) {
                var response = await requestTcs.Task;

                Debug.Assert(response.Length < len); // len includes null terminator
                if (response.Length >= len) {
                    response = response.Substring(0, len - 1);
                }

                return response;
            }
        }

        private Task WriteErrorAsync(string text) =>
            ((IRCallbacks)this).WriteConsoleEx(text + "\n", OutputType.Error, CancellationToken.None);

        private Task WriteErrorAsync(string format, params object[] args) =>
            WriteErrorAsync(string.Format(CultureInfo.CurrentCulture, format, args));

        Task IRCallbacks.WriteConsoleEx(string buf, OutputType otype, CancellationToken ct) {
            Output?.Invoke(this, new ROutputEventArgs(otype, buf));
            return Task.CompletedTask;
        }

        /// <summary>
        /// Displays error message
        /// </summary>
        Task IRCallbacks.ShowMessage(string message, CancellationToken ct) {
            var callback = _callback;
            return callback != null ? callback.ShowErrorMessage(message, ct) : Task.CompletedTask;
        }

        /// <summary>
        /// Called as a result of R calling R API 'YesNoCancel' callback
        /// </summary>
        /// <returns>Codes that match constants in RApi.h</returns>
        public async Task<YesNoCancel> YesNoCancel(IReadOnlyList<IRContext> contexts, string s, CancellationToken ct) {

            MessageButtons buttons = await ((IRCallbacks)this).ShowDialog(contexts, s, MessageButtons.YesNoCancel, ct);
            switch (buttons) {
                case MessageButtons.No:
                    return Client.YesNoCancel.No;
                case MessageButtons.Cancel:
                    return Client.YesNoCancel.Cancel;
            }
            return Client.YesNoCancel.Yes;
        }

        /// <summary>
        /// Called when R wants to display generic Windows MessageBox. 
        /// Graph app may call Win32 API directly rather than going via R API callbacks.
        /// </summary>
        /// <returns>Pressed button code</returns>
        async Task<MessageButtons> IRCallbacks.ShowDialog(IReadOnlyList<IRContext> contexts, string s, MessageButtons buttons, CancellationToken hostCancellationToken) {
            await TaskUtilities.SwitchToBackgroundThread();

            OnMutated();
            var callback = _callback;
            if (callback != null) {
                return await callback.ShowMessageAsync(s, buttons, hostCancellationToken);
            }

            return MessageButtons.OK;
        }

        Task IRCallbacks.Busy(bool which, CancellationToken ct) {
            return Task.CompletedTask;
        }

        Task IRCallbacks.Plot(PlotMessage plot, CancellationToken ct) {
            var callback = _callback;
            return callback != null ? callback.Plot(plot, ct) : Task.CompletedTask;
        }

        Task<LocatorResult> IRCallbacks.Locator(Guid deviceId, CancellationToken ct) {
            var callback = _callback;
            return callback != null ? callback.Locator(deviceId, ct) : Task.FromResult(LocatorResult.CreateNotClicked());
        }

        Task<PlotDeviceProperties> IRCallbacks.PlotDeviceCreate(Guid deviceId, CancellationToken ct) {
            var callback = _callback;
            return callback != null ? callback.PlotDeviceCreate(deviceId, ct) : Task.FromResult(PlotDeviceProperties.Default);
        }

        Task IRCallbacks.PlotDeviceDestroy(Guid deviceId, CancellationToken ct) {
            var callback = _callback;
            return callback != null ? callback.PlotDeviceDestroy(deviceId, ct) : Task.CompletedTask;
        }

        /// <summary>
        /// Asks VS to open specified URL in the help window browser
        /// </summary>
        /// <param name="url"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        async Task IRCallbacks.WebBrowser(string url, CancellationToken cancellationToken) {
            var callback = _callback;
            if (callback != null) {
                var newUrl = await BrokerClient.HandleUrlAsync(url, cancellationToken);
                await callback.ShowHelpAsync(newUrl, cancellationToken);
            }
        }

        Task IRCallbacks.ViewLibrary(CancellationToken cancellationToken) {
            var callback = _callback;
            return callback?.ViewLibraryAsync(cancellationToken);
        }

        Task IRCallbacks.ShowFile(string fileName, string tabName, bool deleteFile, CancellationToken cancellationToken) {
            var callback = _callback;
            return callback?.ViewFile(fileName, tabName, deleteFile, cancellationToken);
        }

        void IRCallbacks.DirectoryChanged() {
            if (_processingChangeDirectoryCommand) {
                DirectoryChanged?.Invoke(this, EventArgs.Empty);
                _processingChangeDirectoryCommand = false;
            }
        }

        Task IRCallbacks.ViewObject(string obj, string title, CancellationToken cancellationToken) {
            var callback = _callback;
            return callback?.ViewObjectAsync(obj, title, cancellationToken) ?? Task.CompletedTask;
        }

        void IRCallbacks.PackagesInstalled() {
            PackagesInstalled?.Invoke(this, EventArgs.Empty);
        }

        void IRCallbacks.PackagesRemoved() {
            PackagesRemoved?.Invoke(this, EventArgs.Empty);
        }

        Task<string> IRCallbacks.SaveFileAsync(string remoteFileName, string localPath, byte[] data, CancellationToken cancellationToken) {
            var callback = _callback;
            return callback != null ? callback.SaveFileAsync(remoteFileName, localPath, data, cancellationToken) : Task.FromResult(string.Empty);
        }

        private class BeforeInitializedRExpressionEvaluator : IRExpressionEvaluator {
            private readonly RSession _session;

            public BeforeInitializedRExpressionEvaluator(RSession session) {
                _session = session;
            }

            public Task<REvaluationResult> EvaluateAsync(string expression, REvaluationKind kind, CancellationToken cancellationToken = new CancellationToken()) 
                => _session.EvaluateAsync(expression, kind, false, cancellationToken);
        }

        private class BrokerTransaction : IRSessionSwitchBrokerTransaction {
            private readonly RSession _session;
            private RHost _hostToSwitch;

            public bool IsSessionDisposed => _session._disposeToken.IsDisposed;

            public BrokerTransaction(RSession session) {
                _session = session;
            }

            public async Task ConnectToNewBrokerAsync(CancellationToken cancellationToken) {
                using (_session._disposeToken.Link(ref cancellationToken)) {
                    var startupInfo = _session._startupInfo;
                    // host requires _startupInfo, so proceed only if session was started at least once
                    var connectionInfo = new BrokerConnectionInfo(startupInfo.Name, _session, startupInfo.RHostCommandLineArguments);
                    _hostToSwitch = await _session.BrokerClient.ConnectAsync(connectionInfo, cancellationToken);
                }
            }

            public async Task CompleteSwitchingBrokerAsync(CancellationToken cancellationToken) {
                using (_session._disposeToken.Link(ref cancellationToken)) {
                    try {
                        var brokerClient = _session.BrokerClient;
                        var startupInfo = _session._startupInfo;
                        var host = _session._host;
                        var hostRunTask = _session._hostRunTask;

                        // host may be null if previous attempts to start it have failed
                        if (host != null) {
                            // Detach RHost from RSession
                            host.DetachCallback();

                            // Cancel all current requests
                            // If can't be canceled in 10s - just ignore, old host will be stopped later
                            await Task.WhenAny(_session.CancelAllAsync(cancellationToken), Task.Delay(10000, cancellationToken)).Unwrap();
                        }

                        // Start new RHost
                        await _session.StartHostAsyncBackground(_hostToSwitch, cancellationToken);

                        // Shut down the old host, gracefully if possible, and wait for old hostRunTask to exit;
                        if (hostRunTask != null) {
                            await StopHostAsync(brokerClient, startupInfo?.Name, host, hostRunTask);
                        }
                        host?.Dispose();

                        if (hostRunTask != null) {
                            await hostRunTask;
                        }
                    } finally {
                        _hostToSwitch = null;
                    }
                }
            }

            public void Dispose() {
                _hostToSwitch?.Dispose();
            }
        }
    }
}