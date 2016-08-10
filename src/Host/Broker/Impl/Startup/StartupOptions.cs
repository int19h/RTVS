// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO.Pipes;

namespace Microsoft.R.Host.Broker.Startup {
    public class StartupOptions {
        public string Name { get; set; }

        public bool AutoSelectPort { get; set; }

        /// <seealso cref="AnonymousPipeServerStream.GetClientHandleAsString"/> 
        public string WriteServerUrlsToPipe { get; set; }
    }
}
