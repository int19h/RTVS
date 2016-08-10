﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Concurrent;
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

        private Process _brokerProcess;

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

            if (_brokerProcess != null && !_brokerProcess.HasExited) {
                return;
            }

            string rhostBrokerExe = Path.Combine(_rhostDirectory, RHostBrokerExe);
            if (!File.Exists(rhostBrokerExe)) {
                throw new RHostBinaryMissingException();
            }

            await TaskUtilities.SwitchToBackgroundThread();

            var uriPipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);

            var psi = new ProcessStartInfo {
                FileName = rhostBrokerExe,
                UseShellExecute = false,
                Arguments =
                    //$" --server.urls {Broker.BaseAddress}" +
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

            _brokerProcess = Process.Start(psi);
            _brokerProcess.EnableRaisingEvents = true;
            _brokerProcess.Exited += delegate { cts.Cancel(); };

            var buffer = new byte[0x1000];
            int count;
            try {
                count = await uriPipe.ReadAsync(buffer, 0, buffer.Length, cts.Token);
            } catch (OperationCanceledException) {
                try {
                    _brokerProcess.Kill();
                    _brokerProcess = null;
                } catch (Exception) {
                }

                throw new RHostTimeoutException("Timed out while waiting for broker process to report its endpoint URI");
            }

            string serverUri = Encoding.UTF8.GetString(buffer).TrimEnd();
            Broker.BaseAddress = new Uri(serverUri);

            uriPipe.DisposeLocalCopyOfClientHandle();


            //for (int i = 0; i < 100; ++i) {
            //    await Task.Delay(1000);
            //    try {
            //        await PingAsync();
            //        //Task.Run(PingWorker).DoNotWait();
            //        return;
            //    } catch (OperationCanceledException) {
            //    }
            //}

            //try {
            //    _brokerProcess.Kill();
            //    _brokerProcess = null;
            //} catch (Exception) {
            //}

            //throw new RHostTimeoutException("Couldn't start broker process");
        }
    }
}
