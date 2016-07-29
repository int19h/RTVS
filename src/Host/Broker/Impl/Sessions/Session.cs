// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Diagnostics;
using Microsoft.R.Host.Broker.Tunneling;

namespace Microsoft.R.Host.Broker.Sessions {
    internal class Session {
        private Process _process;

        public Guid Id { get; }

        public string InterpreterId { get; }

        public RHostPipe Pipe { get; }

        public Session(Guid id, string interpreterId) {
            Id = id;
            InterpreterId = interpreterId;
        }

        public void Start() {

        }

    }
}
