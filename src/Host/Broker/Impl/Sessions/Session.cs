// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Diagnostics;
using System.Security.Principal;
using Microsoft.R.Host.Broker.Interpreters;
using Microsoft.R.Host.Broker.Tunneling;

namespace Microsoft.R.Host.Broker.Sessions {
    internal class Session {
        private Process _process;

        public Guid Id { get; }

        public Interpreter Interpreter { get; }

        public IIdentity User { get; }

        public RHostPipe Pipe { get; }

        public Process Process => _process;

        public Session(Guid id, Interpreter interpreter, IIdentity user) {
            Id = id;
            Interpreter = interpreter;
            User = user;
        }

        public void Start() {

        }
    }
}
