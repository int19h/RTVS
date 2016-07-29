// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Microsoft.R.Host.Broker.Interpreters {
    public class Interpreter {
        public InterpreterManager Manager { get; }

        public InterpreterInfo Info { get; }

        public Interpreter(InterpreterManager manager, InterpreterInfo info) {
            Manager = manager;
            Info = info;
        }
    }
}
