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
using Newtonsoft.Json;

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
                using (var serverUriPipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable)) {
                    var psi = new ProcessStartInfo {
                        FileName = rhostBrokerExe,
                        UseShellExecute = false,
                        Arguments =
                            $" --startup:name \"{_name}\"" +
                            $" --startup:autoSelectPort true" +
                            $" --startup:writeServerUrlsToPipe {serverUriPipe.GetClientHandleAsString()}" +
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

                    var cts = new CancellationTokenSource(10000);
                    process.Exited += delegate {
                        cts.Cancel();
                        _isConnected = false;
                    };

                    var serverUriData = new MemoryStream();
                    try {
                        // Pipes are special in that a zero-length read is not an indicator of end-of-stream.
                        // Stream.CopyTo uses a zero-length read as indicator of end-of-stream, so it cannot 
                        // be used here. Instead, copy the data manually, using PipeStream.IsConnected to detect
                        // when the other side has finished writing and closed the pipe.
                        var buffer = new byte[0x1000];
                        do {
                            int count = await serverUriPipe.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                            serverUriData.Write(buffer, 0, count);
                            serverUriPipe.DisposeLocalCopyOfClientHandle();
                        } while (serverUriPipe.IsConnected);
                    } catch (OperationCanceledException) {
                        throw new RHostTimeoutException("Timed out while waiting for broker process to report its endpoint URI");
                    }

                    var serverUri = JsonConvert.DeserializeObject<Uri[]>(Encoding.UTF8.GetString(serverUriData.ToArray()));
                    if (serverUri.Length != 1) {
                        throw new RHostDisconnectedException("Unexpected number of endpoint URIs received from broker.");
                    }

                    Broker.BaseAddress = serverUri[0];
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
