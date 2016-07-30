// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Common.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.R.Host.Broker.Interpreters;
using Microsoft.R.Host.Broker.Pipes;

namespace Microsoft.R.Host.Broker.Sessions {
    public class Session {
        private const string RHostExe = "Microsoft.R.Host.exe";

        private Process _process;
        private MessagePipe _pipe;

        public SessionManager Manager { get; }

        public Guid Id { get; }

        public Interpreter Interpreter { get; }

        public IIdentity User { get; }

        public Process Process => _process;

        public SessionInfo Info => new SessionInfo {
            Id = Id,
            InterpreterId = Interpreter.Info.Id
        };

        internal Session(SessionManager manager, Guid id, Interpreter interpreter, IIdentity user) {
            Manager = manager;
            Id = id;
            Interpreter = interpreter;
            User = user;
        }

        public void Start(IUrlHelper urlHelper) {
            string brokerPath = Path.GetDirectoryName(typeof(Program).Assembly.GetAssemblyPath());
            string rhostExePath = Path.Combine(brokerPath, RHostExe);

            //var pipeWsUri = new Uri(new Uri("ws://localhost:5000"), urlHelper.Action("Get", "Pipes", new { id = Id }));

            var pipeWsUri = $"ws://localhost:5000/pipes/{Id}";

            var psi = new ProcessStartInfo(rhostExePath) {
                UseShellExecute = false,
                CreateNoWindow = false,
                Arguments = $"--rhost-name BrokerSession{Id} --rhost-connect {pipeWsUri}"
            };

            var shortHome = new StringBuilder(NativeMethods.MAX_PATH);
            NativeMethods.GetShortPathName(Interpreter.Info.Path, shortHome, shortHome.Capacity);
            psi.EnvironmentVariables["R_HOME"] = shortHome.ToString();
            psi.EnvironmentVariables["PATH"] = Interpreter.Info.BinPath + ";" + Environment.GetEnvironmentVariable("PATH");

            _pipe = new MessagePipe();
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
