﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Common.Core;
using Microsoft.Extensions.Logging;
using Microsoft.R.Host.Broker.Interpreters;
using Microsoft.R.Host.Broker.Pipes;
using Microsoft.R.Host.Broker.Startup;

namespace Microsoft.R.Host.Broker.Sessions {
    public class Session {
        private const string RHostExe = "Microsoft.R.Host.exe";

        private static readonly byte[] _endMessage;

        private Process _process;
        private volatile MessagePipe _pipe;

        public SessionManager Manager { get; }

        public string Id { get; }

        public Interpreter Interpreter { get; }

        public IIdentity User { get; }

        public Process Process => _process;

        public SessionInfo Info => new SessionInfo {
            Id = Id,
            InterpreterId = Interpreter.Info.Id
        };

        static Session() {
            using (var stream = new MemoryStream()) {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true)) {
                    writer.Write(ulong.MaxValue - 1);
                    writer.Write(0UL);
                    writer.Write("!End".ToCharArray());
                    writer.Write((byte)0);
                    writer.Write("[]".ToCharArray());
                    writer.Write((byte)0);
                }

                _endMessage = stream.ToArray();
            }
        }

        internal Session(SessionManager manager, string id, Interpreter interpreter, IIdentity user) {
            Manager = manager;
            Id = id;
            Interpreter = interpreter;
            User = user;
        }

        public void StartHost(ILogger outputLogger, ILogger messageLogger) {
            string brokerPath = Path.GetDirectoryName(typeof(Program).Assembly.GetAssemblyPath());
            string rhostExePath = Path.Combine(brokerPath, RHostExe);

            var psi = new ProcessStartInfo(rhostExePath) {
                UseShellExecute = false,
                CreateNoWindow = false,
                Arguments = $"--rhost-name {Id}",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var shortHome = new StringBuilder(NativeMethods.MAX_PATH);
            NativeMethods.GetShortPathName(Interpreter.Info.Path, shortHome, shortHome.Capacity);
            psi.EnvironmentVariables["R_HOME"] = shortHome.ToString();
            psi.EnvironmentVariables["PATH"] = Interpreter.Info.BinPath + ";" + Environment.GetEnvironmentVariable("PATH");

            _process = new Process();
            _process.StartInfo = psi;
            _process.EnableRaisingEvents = true;

            _process.ErrorDataReceived += (sender, e) => {
                if (outputLogger != null) {
                    outputLogger.LogTrace(e.Data);
                }
            };

            _process.Exited += delegate {
                _pipe = null;
            };

            var winUser = User as WindowsIdentity;
            using (winUser != null ? winUser.Impersonate() : null) {
                _process.Start();
            }

            _process.BeginErrorReadLine();

            _pipe = new MessagePipe(messageLogger);
            var hostEnd = _pipe.ConnectHost();

            ClientToHostWorker(_process.StandardInput.BaseStream, hostEnd).DoNotWait();
            HostToClientWorker(_process.StandardOutput.BaseStream, hostEnd).DoNotWait();
        }

        //public void StartHost() {
        //    string brokerPath = Path.GetDirectoryName(typeof(Program).Assembly.GetAssemblyPath());
        //    string rhostExePath = Path.Combine(brokerPath, RHostExe);

        //    Stream stdin, stdout;
        //    int pid = ProcessHelpers.StartProcessAsUser(User, rhostExePath, $"--rhost-name {Id}", Interpreter.BinPath, out stdin, out stdout);

        //    _pipe = new MessagePipe();

        //    _process = Process.GetProcessById(pid);
        //    _process.EnableRaisingEvents = true;
        //    _process.Exited += delegate { _pipe = null; };

        //    var hostEnd = _pipe.ConnectHost();

        //    ClientToHostWorker(stdin, hostEnd).DoNotWait();
        //    HostToClientWorker(stdout, hostEnd).DoNotWait();
        //}

        public void KillHost() {
            try {
                _process?.Kill();
            } catch (Win32Exception) {
            } catch (InvalidOperationException) {
            }

            _process = null;
        }

        public IMessagePipeEnd ConnectClient() {
            if (_pipe == null) {
                throw new InvalidOperationException("Host process not started");
            }

            return _pipe.ConnectClient();
        }

        private async Task ClientToHostWorker(Stream stream, IMessagePipeEnd pipe) {
            while (true) {
                var message = await pipe.ReadAsync(Program.CancellationToken);
                var sizeBuf = BitConverter.GetBytes(message.Length);
                try {
                    await stream.WriteAsync(sizeBuf, 0, sizeBuf.Length);
                    await stream.WriteAsync(message, 0, message.Length);
                    await stream.FlushAsync();
                } catch (IOException) {
                    break;
                }
            }
        }

        private async Task HostToClientWorker(Stream stream, IMessagePipeEnd pipe) {
            var sizeBuf = new byte[sizeof(int)];
            while (true) {
                if (!await FillFromStreamAsync(stream, sizeBuf)) {
                    break;
                }
                int size = BitConverter.ToInt32(sizeBuf, 0);

                var message = new byte[size];
                if (!await FillFromStreamAsync(stream, message)) {
                    break;
                }

                pipe.Write(message);
            }

            pipe.Write(_endMessage);
        }

        private static async Task<bool> FillFromStreamAsync(Stream stream, byte[] buffer) {
            for (int index = 0, count = buffer.Length; count != 0;) {
                int read = await stream.ReadAsync(buffer, index, count);
                if (read == 0) {
                    return false;
                }

                index += read;
                count -= read;
            }

            return true;
        }
    }
}
