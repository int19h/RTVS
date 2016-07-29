// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Common.Core;
using Microsoft.Common.Core.Logging;

namespace Microsoft.R.Host.Client.Host {
    internal sealed class RemoteRHostConnector : RHostConnector {
        public RemoteRHostConnector(Uri brokerUri)
            : base(brokerUri.Fragment) {

            CreateHttpClient();
            Broker.BaseAddress = brokerUri;
        }

        protected override Task ConnectToBrokerAsync() {
            return Task.CompletedTask;
        }
    }
}
