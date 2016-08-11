// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Server;
using Microsoft.R.Host.Broker.Lifetime;
using Microsoft.R.Host.Broker.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.IO.Pipes;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Newtonsoft.Json;
using System.Text;

namespace Microsoft.R.Host.Broker.Startup {
    public class Program {
        private static readonly ILoggerFactory _loggerFactory = new LoggerFactory();
        private static ILogger _logger;
        private static readonly StartupOptions _startupOptions = new StartupOptions();
        private static readonly CancellationTokenSource _cts = new CancellationTokenSource();

        internal static IConfigurationRoot Configuration { get; private set; }

        public static CancellationToken CancellationToken => _cts.Token;

        static Program() {
        }

        public static void Main(string[] args) {
            var configBuilder = new ConfigurationBuilder().AddCommandLine(args);
            Configuration = configBuilder.Build();

            string configFile = Configuration["config"];
            if (configFile != null) {
                configBuilder.AddJsonFile(configFile, optional: false);
                Configuration = configBuilder.Build();
            }

            ConfigurationBinder.Bind(Configuration.GetSection("startup"), _startupOptions);

            _loggerFactory
                .AddDebug()
                .AddConsole(LogLevel.Trace)
                .AddProvider(new FileLoggerProvider(_startupOptions.Name));
            _logger = _loggerFactory.CreateLogger<Program>();

            if (_startupOptions.Name != null) {
                _logger.LogInformation($"Broker name '{_startupOptions.Name}' assigned");
            }

            if (!_startupOptions.AutoSelectPort) {
                CreateWebHost().Run();
            } else {
                // Randomly shuffled sequence of port numbers from the ephemeral port range (per RFC 6335 8.1.2).
                const int ephemeralRangeStart = 49152;
                var rnd = new Random();
                var ports = from port in Enumerable.Range(ephemeralRangeStart, 0x10000 - ephemeralRangeStart)
                            let pos = rnd.NextDouble()
                            orderby pos
                            select port;

                bool foundPort = ports.Any(port => {
                    var webHost = CreateWebHost(port);
                    try {
                        webHost.Run(CancellationToken);
                        return true;
                    } catch (WebListenerException) when (_startupOptions.AutoSelectPort) {
                        _logger.LogInformation($"Port auto-selection: port {port} is already in use, trying next port.");
                        return false;
                    }
                });

                if (!foundPort) {
                    _logger.LogCritical($"Port auto-selection requested, but no ephemeral ports are available.");
                }
            }
        }

        public static IWebHost CreateWebHost(int? autoSelectedPort = null) {
            var webHostBuilder = new WebHostBuilder()
                .UseLoggerFactory(_loggerFactory)
                .UseConfiguration(Configuration)
                //.UseWebListener(options => {
                //    options.Listener.AuthenticationManager.AuthenticationSchemes = AuthenticationSchemes.NTLM | AuthenticationSchemes.AllowAnonymous;
                //    options.Listener.TimeoutManager.MinSendBytesPerSecond = uint.MaxValue;
                //    options.Listener.BufferResponses = false;
                //})
                .UseKestrel(options => {
                    //options.UseConnectionLogging();
                })
                .UseContentRoot(Directory.GetCurrentDirectory())
                .UseStartup<Startup>();

            if (autoSelectedPort != null) {
                var uri = new UriBuilder("http://127.0.0.1") { Port = autoSelectedPort.Value }.Uri;
                _logger.LogInformation($"Port auto-selection requested, trying {uri}.");
                webHostBuilder.UseUrls(uri.ToString());
            }

            var webHost = webHostBuilder.Build();

            var serverAddresses = webHost.ServerFeatures.Get<IServerAddressesFeature>();
            if (autoSelectedPort != null && serverAddresses.Addresses.Count != 1) {
                _logger.LogCritical("Explicit server.urls is not supported in conjunction with port auto-selection.");
                throw new InvalidOperationException();
            }

            string pipeName = _startupOptions.WriteServerUrlsToPipe;
            if (pipeName != null) {
                NamedPipeClientStream pipe;
                try {
                    pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
                    pipe.Connect(10000);
                } catch (IOException ex) {
                    _logger.LogCritical(0, ex, $"Requested to write server.urls to pipe '{pipeName}', but it is not a valid pipe handle.");
                    throw;
                } catch (TimeoutException ex) {
                    _logger.LogCritical(0, ex, $"Requested to write server.urls to pipe '{pipeName}', but timed out while trying to connect to pipe.");
                    throw;
                }

                var applicationLifetime = webHost.Services.GetService<IApplicationLifetime>();
                applicationLifetime.ApplicationStarted.Register(() => {
                    using (pipe) {
                        var serverUriData = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(serverAddresses.Addresses));
                        pipe.Write(serverUriData, 0, serverUriData.Length);
                        pipe.Flush();
                    }

                    _logger.LogInformation($"Wrote server.urls to pipe '{pipeName}'.");
                });
            }

            return webHost;
        }

        public static void Exit() {
            _cts.Cancel();

            Task.Run(async () => {
                // Give cooperative cancellation 10 seconds to shut the process down gracefully,
                // but if it didn't work, just terminate it.
                await Task.Delay(10000);
                Environment.Exit(1);
            });
        }
    }
}
