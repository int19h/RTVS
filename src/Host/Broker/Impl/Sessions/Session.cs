// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using Microsoft.Common.Core;
using Microsoft.R.Host.Broker.Interpreters;
using Microsoft.R.Host.Broker.Tunneling;

namespace Microsoft.R.Host.Broker.Sessions {
    public class Session {
        private const string RHostExe = "Microsoft.R.Host.exe";

        private Process _process;
        private readonly MessagePipe _pipe;

        public Guid Id { get; }

        public Interpreter Interpreter { get; }

        public IIdentity User { get; }

        public Process Process => _process;

        public Session(Guid id, Interpreter interpreter, IIdentity user) {
            Id = id;
            Interpreter = interpreter;
            User = user;
        }

        public void Start() {
            string brokerPath = Path.GetDirectoryName(typeof(Program).Assembly.GetAssemblyPath());
            string rhostExePath = Path.Combine(brokerPath, RHostExe);

            var psi = new ProcessStartInfo(rhostExePath) {
                UseShellExecute = false,
                CreateNoWindow = false,
                Arguments = string.Format("--rhost-name BrokerSession{0} --rhost-connect ws://localhost/tunnels/{0}", Id)
            };

            var shortHome = new StringBuilder(NativeMethods.MAX_PATH);
            NativeMethods.GetShortPathName(Interpreter.Info.Path, shortHome, shortHome.Capacity);
            psi.EnvironmentVariables["R_HOME"] = shortHome.ToString();
            psi.EnvironmentVariables["PATH"] = Interpreter.Info.BinPath + ";" + Environment.GetEnvironmentVariable("PATH");

            _process = Process.Start(psi);
        }

        public IMessagePipeEnd ConnectHost() {
            if (_pipe == null) {
                throw new InvalidOperationException("Host process not started");
            }

            return _pipe.ConnectHost();
        }

        public IMessagePipeEnd ConnectClient() {
            if (_pipe == null) {
                throw new InvalidOperationException("Host process not started");
            }

            return _pipe.ConnectClient();
        }
    }
}
