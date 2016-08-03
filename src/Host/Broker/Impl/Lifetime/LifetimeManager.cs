// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.R.Host.Broker.Lifetime {
    public class LifetimeManager {
        private readonly IOptions<LifetimeOptions> _options;
        private readonly ILogger _logger;

        private CancellationTokenSource _cts;

        public LifetimeManager(IOptions<LifetimeOptions> options, ILoggerFactory loggerFactory) {
            _options = options;
            _logger = loggerFactory.CreateLogger<LifetimeManager>();

            Ping();
        }

        public void Ping() {
            var cts = new CancellationTokenSource(_options.Value.PingTimeout);
            cts.Token.Register(() => {
                if (_cts == cts) {
                    _logger.LogWarning("Ping timeout");
                    Program.Exit();
                }
            });
            _cts = cts; 
        }
    }
}
