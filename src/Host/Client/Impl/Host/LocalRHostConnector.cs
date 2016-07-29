// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
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

        private readonly string _rhostDirectory;
        private readonly string _rHome;
        private readonly LinesLog _log;
        private readonly SemaphoreSlim _connectSemaphore = new SemaphoreSlim(1, 1);

        private Process _brokerProcess;
        private bool _isConnected;

        public LocalRHostConnector(string rHome, string rhostDirectory = null)
            : base(InterpreterId) {

            _rhostDirectory = rhostDirectory ?? Path.GetDirectoryName(typeof(RHost).Assembly.GetAssemblyPath());
            _rHome = rHome;
        }

        public override void Dispose() {
            base.Dispose();

            if (IsDisposed) {
                return;
            }

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
                            $" --startup:autoSelectPort true " +
                            $" --startup:writeServerUrlsToPipe {uriPipe.GetClientHandleAsString()}" +
                            $" --lifetime:parentProcessId {Process.GetCurrentProcess().Id}" +
                            $" --R:autoDetect false" +
                            $" --R:interpreters:{InterpreterId}:basePath \"{_rHome}\""
                    };

                    if (!ShowConsole) {
                        psi.CreateNoWindow = true;
                    }

                    var cts = new CancellationTokenSource(5000);

                    process = Process.Start(psi);
                    process.EnableRaisingEvents = true;
                    process.Exited += delegate {
                        cts.Cancel();
                        _isConnected = false;
                    };

                    var buffer = new byte[0x1000];
                    int count;
                    try {
                        count = await uriPipe.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                    } catch (OperationCanceledException) {
                        throw new RHostTimeoutException("Timed out while waiting for broker process to report its endpoint URI");
                    }

                    string serverUri = Encoding.UTF8.GetString(buffer).TrimEnd();
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
