// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Concurrent;
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
    internal sealed class LocalRHostConnector : IRHostConnector {
        public const int DefaultPort = 5118;
        public const string RHostBrokerExe = "Microsoft.R.Host.Broker.exe";
        public const string RBinPathX64 = @"bin\x64";

        private static readonly bool ShowConsole = true; //!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RTVS_HOST_CONSOLE"));
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
        private HttpClient _broker;

        public LocalRHostConnector(string rHome, string rhostDirectory = null) {
            _rhostDirectory = rhostDirectory ?? Path.GetDirectoryName(typeof(RHost).Assembly.GetAssemblyPath());
            _rHome = rHome;
            _log = new LinesLog(FileLogWriter.InTempFolder("Microsoft.R.Host.BrokerConnector"));

            _broker = new HttpClient(new HttpClientHandler { UseDefaultCredentials = true }) {
                Timeout = TimeSpan.FromSeconds(1)
            };
            _broker.DefaultRequestHeaders.Accept.Clear();
            _broker.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        private void ReserveBrokerUri() {
            while (true) {
                var usedPorts = _connectors.Keys;

                int port = DefaultPort;
                while (usedPorts.Contains(port)) {
                    ++port;
                }

                if (_connectors.TryAdd(port, this)) {
                    _broker.BaseAddress = new Uri($"http://localhost:{port}");
                    return;
                }
            }
        }

        public async Task StartBrokerAsync() {
            string rhostBrokerExe = Path.Combine(_rhostDirectory, RHostBrokerExe);
            if (!File.Exists(rhostBrokerExe)) {
                throw new RHostBinaryMissingException();
            }

            await TaskUtilities.SwitchToBackgroundThread();

            ReserveBrokerUri();

            var psi = new ProcessStartInfo {
                FileName = rhostBrokerExe,
                UseShellExecute = false,
                Arguments = $" --server.urls {_broker.BaseAddress}"
            };

            if (!ShowConsole) {
                psi.CreateNoWindow = true;
            }

            _brokerProcess = Process.Start(psi);

            for (int i = 0; i < 100; ++i) {
                await Task.Delay(1000);
                try {
                    await _broker.GetAsync("/interpreters");
                    return;
                } catch (OperationCanceledException) {
                }
            }

            try {
                _brokerProcess.Kill();
            } catch (Exception) {
            }

            throw new RHostTimeoutException("Couldn't start broker process");
        }

        public async Task<RHost> Connect(string name, IRCallbacks callbacks, string rCommandLineArguments = null, int timeout = 3000, CancellationToken cancellationToken = new CancellationToken()) {
            await TaskUtilities.SwitchToBackgroundThread();

            if (_brokerProcess?.HasExited != false) {
                throw new InvalidOperationException("Broker process is not running - call StartBrokerAsync first");
            }

            rCommandLineArguments = rCommandLineArguments ?? string.Empty;

            var sessionId = Guid.NewGuid();
            var request = new { InterpreterId = "" };
            var requestContent = new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json");

            (await _broker.PutAsync($"/sessions/{sessionId}", requestContent, cancellationToken)).EnsureSuccessStatusCode();

            var wsClient = new WebSocketClient {
                KeepAliveInterval = HeartbeatTimeout,
                SubProtocols = { "Microsoft.R.Host" },
                ConfigureRequest = httpRequest => {
                    httpRequest.AuthenticationLevel = AuthenticationLevel.MutualAuthRequested;
                    httpRequest.Credentials = CredentialCache.DefaultNetworkCredentials;
                }
            };

            var pipeUri = new UriBuilder(_broker.BaseAddress) {
                Scheme = "ws",
                Path = $"sessions/{sessionId}/pipe"
            }.Uri;
            var socket = await wsClient.ConnectAsync(pipeUri, cancellationToken);

            var transport = new WebSocketMessageTransport(socket);

            var cts = new CancellationTokenSource();
            cts.Token.Register(() => {
                _log.RHostProcessExited();
            });

            var host = new RHost(name, callbacks, transport, null, cts);
            return host;
        }
    }
}
