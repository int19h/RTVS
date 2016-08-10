// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Common.Core;
using Microsoft.Common.Core.Logging;

namespace Microsoft.R.Host.Client.Host {
    internal sealed class LocalRHostConnector : RHostConnector {
        private const int DefaultPort = 5118;
        private const string RHostBrokerExe = "Microsoft.R.Host.Broker.exe";
        private const string RBinPathX64 = @"bin\x64";
        private const string InterpreterId = "local";

        private static readonly bool ShowConsole = true; //string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RTVS_HOST_CONSOLE"));
        private static readonly TimeSpan HeartbeatTimeout =
#if DEBUG
            // In debug mode, increase the timeout significantly, so that when the host is paused in debugger,
            // the client won't immediately timeout and disconnect.
            TimeSpan.FromMinutes(10);
#else
            TimeSpan.FromSeconds(5);
#endif

        private static readonly NetworkCredential _credentials = new NetworkCredential("RTVS", Guid.NewGuid().ToString());

        private readonly string _name;
        private readonly string _rhostDirectory;
        private readonly string _rHome;
        private readonly SemaphoreSlim _connectSemaphore = new SemaphoreSlim(1, 1);

        private Process _brokerProcess;
        private bool _isConnected;

        public LocalRHostConnector(string name, string rHome, string rhostDirectory = null)
            : base(InterpreterId) {

            _name = name;
            _rhostDirectory = rhostDirectory ?? Path.GetDirectoryName(typeof(RHost).Assembly.GetAssemblyPath());
            _rHome = rHome;
        }

        public override void Dispose() {
            if (IsDisposed) {
                return;
            }

            base.Dispose();

            if (_brokerProcess != null) {
                if (!_brokerProcess.HasExited) {
                    try {
                        _brokerProcess.Kill();
                    } catch (Win32Exception) {
                    } catch (InvalidOperationException) {
                    }

                    _brokerProcess = null;
                }
            }
        }

        protected override HttpClientHandler GetHttpClientHandler() {
            return new HttpClientHandler {
                Credentials = _credentials
            };
        }

        protected override void ConfigureWebSocketRequest(HttpWebRequest request) {
            request.AuthenticationLevel = AuthenticationLevel.MutualAuthRequested;
            request.Credentials = _credentials;
        }

        protected override async Task ConnectToBrokerAsync() {
            if (IsDisposed) {
                throw new ObjectDisposedException(typeof(LocalRHostConnector).FullName);
            }

            await TaskUtilities.SwitchToBackgroundThread();

            try {
                await _connectSemaphore.WaitAsync();
                if (!_isConnected) {
                    CreateHttpClient();
                    await ConnectToBrokerWorker();
                }
            } finally {
                _connectSemaphore.Release();
            }
        }

        private async Task ConnectToBrokerWorker() {
            string rhostBrokerExe = Path.Combine(_rhostDirectory, RHostBrokerExe);
            if (!File.Exists(rhostBrokerExe)) {
                throw new RHostBinaryMissingException();
            }

            Process process = null;
            try {
                using (var uriPipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable)) {
                    var psi = new ProcessStartInfo {
                        FileName = rhostBrokerExe,
                        UseShellExecute = false,
                        Arguments =
                            $" --startup:name \"{_name}\"" +
                            $" --startup:autoSelectPort true" +
                            $" --startup:writeServerUrlsToPipe {uriPipe.GetClientHandleAsString()}" +
                            $" --lifetime:parentProcessId {Process.GetCurrentProcess().Id}" +
                            $" --security:secret \"{_credentials.Password}\"" +
                            $" --R:autoDetect false" +
                            $" --R:interpreters:{InterpreterId}:basePath \"{_rHome}\""
                    };

                    if (!ShowConsole) {
                        psi.CreateNoWindow = true;
                    }

                    process = Process.Start(psi);
                    process.EnableRaisingEvents = true;

                    var cts = new CancellationTokenSource(50000);
                    process.Exited += delegate {
                        cts.Cancel();
                        _isConnected = false;
                    };

                    uriPipe.DisposeLocalCopyOfClientHandle();

                    var serverUriData = new MemoryStream();
                    try {
                        await uriPipe.CopyToAsync(serverUriData, 0x1000, cts.Token);
                    } catch (OperationCanceledException) {
                        throw new RHostTimeoutException("Timed out while waiting for broker process to report its endpoint URI");
                    }

                    string serverUri = Encoding.UTF8.GetString(serverUriData.ToArray()).TrimEnd();
                    Broker.BaseAddress = new Uri(serverUri);
                }

                _brokerProcess = process;
                _isConnected = true;
            } finally {
                if (!_isConnected) {
                    try {
                        process?.Kill();
                    } catch (Exception) {
                    }
                }
            }
        }
    }
}
