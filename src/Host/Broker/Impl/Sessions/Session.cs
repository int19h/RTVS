﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Common.Core;
using Microsoft.Extensions.Logging;
using Microsoft.R.Host.Broker.Interpreters;
using Microsoft.R.Host.Broker.Pipes;
using Microsoft.R.Host.Protocol;
using Microsoft.R.Host.Broker.Startup;

namespace Microsoft.R.Host.Broker.Sessions {
    public class Session {
        private const string RHostExe = "Microsoft.R.Host.exe";

        private static readonly byte[] _endMessage;
        private readonly ILogger _sessionLogger;
        private Process _process;
        private MessagePipe _pipe;
        private volatile IMessagePipeEnd _hostEnd;

        public SessionManager Manager { get; }

        public IIdentity User { get; }

        /// <remarks>
        /// Unique for a given <see cref="User"/> only.
        /// </remarks>
        public string Id { get; }

        public Interpreter Interpreter { get; }

        public string CommandLineArguments { get; }

        private volatile SessionState _state;

        public SessionState State {
            get {
                return _state;
            }
            set {
                var oldState = _state;
                _state = value;
                StateChanged?.Invoke(this, new SessionStateChangedEventArgs(oldState, value));
            }
        }

        public event EventHandler<SessionStateChangedEventArgs> StateChanged;

        public Process Process => _process;

        public SessionInfo Info => new SessionInfo {
            Id = Id,
            InterpreterId = Interpreter.Id,
            CommandLineArguments = CommandLineArguments,
        };

        static Session() {
            using (var stream = new MemoryStream()) {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true)) {
                    writer.Write(ulong.MaxValue - 1);
                    writer.Write(0UL);
                    writer.Write("!Shutdown".ToCharArray());
                    writer.Write((byte)0);
                    writer.Write("[false]".ToCharArray());
                    writer.Write((byte)0);
                }

                _endMessage = stream.ToArray();
            }
        }

        internal Session(SessionManager manager, IIdentity user, string id, Interpreter interpreter, string commandLineArguments, ILogger sessionLogger, ILogger messageLogger) {
            Manager = manager;
            Interpreter = interpreter;
            User = user;
            Id = id;
            CommandLineArguments = commandLineArguments;
            _sessionLogger = sessionLogger;

            _pipe = new MessagePipe(messageLogger);
        }

        public void StartHost(SecureString password, string profilePath, ILogger outputLogger) {
            if (_hostEnd != null) {
                throw new InvalidOperationException("Host process is already running");
            }

            string brokerPath = Path.GetDirectoryName(typeof(Program).Assembly.GetAssemblyPath());
            string rhostExePath = Path.Combine(brokerPath, RHostExe);
            string arguments = $"--rhost-name \"{Id}\" {CommandLineArguments}";
            var username = new StringBuilder(NativeMethods.CREDUI_MAX_USERNAME_LENGTH + 1);
            var domain = new StringBuilder(NativeMethods.CREDUI_MAX_PASSWORD_LENGTH + 1);

            ProcessStartInfo psi = new ProcessStartInfo(rhostExePath) {
                UseShellExecute = false,
                CreateNoWindow = false,
                Arguments = arguments,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                LoadUserProfile = true
            };
            
            var useridentity = User as WindowsIdentity;
            if (useridentity != null && WindowsIdentity.GetCurrent().User != useridentity.User && password != null) {
                uint error = NativeMethods.CredUIParseUserName(User.Name, username, username.Capacity, domain, domain.Capacity);
                if (error != 0) {
                    _sessionLogger.LogError(Resources.Error_UserNameParse, User.Name, error);
                    throw new ArgumentException(string.Format(Resources.Error_UserNameParse, User.Name, error));
                }

                psi.Domain = domain.ToString();
                psi.UserName = username.ToString();
                psi.Password = password;

                _sessionLogger.LogTrace(Resources.Trace_EnvironmentVariableCreationBegin, User.Name, profilePath);
                // if broker and rhost are run as different users recreate user environment variables.
                psi.EnvironmentVariables["USERNAME"] = username.ToString();
                _sessionLogger.LogTrace(Resources.Trace_EnvironmentVariable, "USERNAME", psi.EnvironmentVariables["USERNAME"]);

                psi.EnvironmentVariables["HOMEDRIVE"] = profilePath.Substring(0, 2);
                _sessionLogger.LogTrace(Resources.Trace_EnvironmentVariable, "HOMEDRIVE", psi.EnvironmentVariables["HOMEDRIVE"]);

                psi.EnvironmentVariables["HOMEPATH"] = profilePath.Substring(2);
                _sessionLogger.LogTrace(Resources.Trace_EnvironmentVariable, "HOMEPATH", psi.EnvironmentVariables["HOMEPATH"]);

                psi.EnvironmentVariables["USERPROFILE"] = $"{psi.EnvironmentVariables["HOMEDRIVE"]}{psi.EnvironmentVariables["HOMEPATH"]}";
                _sessionLogger.LogTrace(Resources.Trace_EnvironmentVariable, "USERPROFILE", psi.EnvironmentVariables["USERPROFILE"]);

                psi.EnvironmentVariables["APPDATA"] = $"{psi.EnvironmentVariables["USERPROFILE"]}\\AppData\\Roaming";
                _sessionLogger.LogTrace(Resources.Trace_EnvironmentVariable, "APPDATA", psi.EnvironmentVariables["APPDATA"]);

                psi.EnvironmentVariables["LOCALAPPDATA"] = $"{psi.EnvironmentVariables["USERPROFILE"]}\\AppData\\Local";
                _sessionLogger.LogTrace(Resources.Trace_EnvironmentVariable, "LOCALAPPDATA", psi.EnvironmentVariables["LOCALAPPDATA"]);

                psi.EnvironmentVariables["TEMP"] = $"{psi.EnvironmentVariables["LOCALAPPDATA"]}\\Temp";
                _sessionLogger.LogTrace(Resources.Trace_EnvironmentVariable, "TEMP", psi.EnvironmentVariables["TEMP"]);

                psi.EnvironmentVariables["TMP"] = $"{psi.EnvironmentVariables["LOCALAPPDATA"]}\\Temp";
                _sessionLogger.LogTrace(Resources.Trace_EnvironmentVariable, "TMP", psi.EnvironmentVariables["TMP"]);
            }

            var shortHome = new StringBuilder(NativeMethods.MAX_PATH);
            NativeMethods.GetShortPathName(Interpreter.Info.Path, shortHome, shortHome.Capacity);
            psi.EnvironmentVariables["R_HOME"] = shortHome.ToString();
            psi.EnvironmentVariables["PATH"] = Interpreter.Info.BinPath + ";" + Environment.GetEnvironmentVariable("PATH");

            psi.WorkingDirectory = Path.GetDirectoryName(rhostExePath);

            _process = new Process {
                StartInfo = psi,
                EnableRaisingEvents = true,
            };

            _process.ErrorDataReceived += (sender, e) => {
                var process = (Process)sender;
                outputLogger?.LogTrace(Resources.Trace_ErrorDataReceived, process.Id, e.Data);
            };

            _process.Exited += delegate {
                _hostEnd?.Dispose();
                _hostEnd = null;
                State = SessionState.Dormant;
            };

            _sessionLogger.LogInformation(Resources.Info_StartingRHost, Id, User.Name, rhostExePath, arguments);
            _process.Start();
            _sessionLogger.LogInformation(Resources.Info_StartedRHost, Id, User.Name);

            _process.BeginErrorReadLine();

            var hostEnd = _pipe.ConnectHost(_process.Id);
            _hostEnd = hostEnd;

            ClientToHostWorker(_process.StandardInput.BaseStream, hostEnd).DoNotWait();
            HostToClientWorker(_process.StandardOutput.BaseStream, hostEnd).DoNotWait();
        }

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
                throw new InvalidOperationException(string.Format(Resources.Error_RHostFailedToStart, Id));
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

            //pipe.Write(_endMessage);
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
