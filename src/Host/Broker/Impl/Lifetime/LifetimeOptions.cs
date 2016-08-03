// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Generic;

namespace Microsoft.R.Host.Broker.Lifetime {
    public class LifetimeOptions {
        public int PingTimeout { get; set; } = 5000;
    }
}
