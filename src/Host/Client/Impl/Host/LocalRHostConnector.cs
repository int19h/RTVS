// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebSockets.Client;
using Microsoft.Common.Core;
using Microsoft.Common.Core.Logging;
using Newtonsoft.Json;
using static System.FormattableString;

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

        private static readonly ConcurrentDictionary<int, LocalRHostConnector> _connectors = new ConcurrentDictionary<int, LocalRHostConnector>();

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

                LocalRHostConnector connector;
                _connectors.TryRemove(Broker.BaseAddress.Port, out connector);
            }
        }

        private void ReserveBrokerUri() {
            while (true) {
                var usedPorts = _connectors.Keys;

                int port = DefaultPort;
                while (usedPorts.Contains(port)) {
                    ++port;
                }

                if (_connectors.TryAdd(port, this)) {
                    Broker.BaseAddress = new Uri($"http://localhost:{port}");
                    return;
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

            ReserveBrokerUri();

            var psi = new ProcessStartInfo {
                FileName = rhostBrokerExe,
                UseShellExecute = false,
                Arguments =
                    $" --server.urls {Broker.BaseAddress} --lifetime:parentProcessId {Process.GetCurrentProcess().Id}" +
                    $" --R:autoDetect false --R:interpreters:{InterpreterId}:basePath \"{_rHome}\""
            };

            if (!ShowConsole) {
                psi.CreateNoWindow = true;
            }

            _brokerProcess = Process.Start(psi);
            _brokerProcess.EnableRaisingEvents = true;
            _brokerProcess.Exited += delegate {
            };

            for (int i = 0; i < 100; ++i) {
                await Task.Delay(1000);
                try {
                    await PingAsync();
                    //Task.Run(PingWorker).DoNotWait();
                    return;
                } catch (OperationCanceledException) {
                }
            }

            try {
                _brokerProcess.Kill();
                _brokerProcess = null;
            } catch (Exception) {
            }

            throw new RHostTimeoutException("Couldn't start broker process");
        }
    }
}
