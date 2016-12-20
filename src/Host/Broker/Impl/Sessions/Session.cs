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
using Microsoft.Common.Core.Logging;
using Microsoft.Common.Core.OS;
using Microsoft.Extensions.Logging;
using Microsoft.R.Host.Broker.Interpreters;
using Microsoft.R.Host.Broker.Pipes;
using Microsoft.R.Host.Broker.Startup;
using Microsoft.R.Host.Protocol;
using static System.FormattableString;
using System.Threading;

namespace Microsoft.R.Host.Broker.Sessions {
    public class Session {
        private const string RHostExe = "Microsoft.R.Host.exe";

        private static readonly byte[] _discardAndShutdownRequest = CreateShutdownRequest(false);
        private static readonly byte[] _saveAndShutdownRequest = CreateShutdownRequest(false);

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

        public bool IsTransient { get; set; }

        private volatile SessionState _state = SessionState.Dormant;
        private readonly object _stateLock = new object();

        private readonly TaskCompletionSource<int> _hostTcs = new TaskCompletionSource<int>();

        public SessionState State {
            get {
                lock (_stateLock) {
                    return _state;
                }
            }
            set {
                lock (_stateLock) {
                    var oldState = _state;
                    if (oldState != value) {
                        _state = value;
                        StateChanged?.Invoke(this, new SessionStateChangedEventArgs(oldState, value));
                    }
                }
            }
        }

        public event EventHandler<SessionStateChangedEventArgs> StateChanged;

        public Process Process => _process;

        public SessionInfo Info => new SessionInfo {
            Id = Id,
            InterpreterId = Interpreter.Id,
            CommandLineArguments = CommandLineArguments,
            IsTransient = IsTransient,
            State = State,
        };

        private static byte[] CreateShutdownRequest(bool saveRData) {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream, Encoding.UTF8)) {
                // Use the largest non-reserved ID, to avoid clashing with client messages.
                writer.Write(ulong.MaxValue - 1);
                writer.Write(0ul);

                var json = (saveRData ? "[true]" : "[false]").ToCharArray();
                writer.Write(json, 0, json.Length);

                writer.Flush();
                stream.Flush();
                return stream.ToArray();
            }

        }

        internal Session(SessionManager manager, IIdentity user, string id, Interpreter interpreter, string commandLineArguments, bool isTransient, ILogger sessionLogger, ILogger messageLogger) {
            Manager = manager;
            Interpreter = interpreter;
            User = user;
            Id = id;
            CommandLineArguments = commandLineArguments;
            IsTransient = isTransient;
            _sessionLogger = sessionLogger;

            _pipe = new MessagePipe(messageLogger);
        }

        public void StartHost(SecureString password, string profilePath, ILogger outputLogger, LogVerbosity verbosity) {
            var useridentity = User as WindowsIdentity;
            // In remote broker User Identity type is always WindowsIdentity
            string suppressUI = (useridentity == null) ? string.Empty : " --suppress-ui ";
            string brokerPath = Path.GetDirectoryName(typeof(Program).Assembly.GetAssemblyPath());
            string rhostExePath = Path.Combine(brokerPath, RHostExe);
            string arguments = Invariant($"{suppressUI}--rhost-name \"{Id}\" --rhost-log-verbosity {(int)verbosity} {CommandLineArguments}");
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

            if (useridentity != null && WindowsIdentity.GetCurrent().User != useridentity.User && password != null) {
                uint error = NativeMethods.CredUIParseUserName(User.Name, username, username.Capacity, domain, domain.Capacity);
                if (error != 0) {
                    _sessionLogger.LogError(Resources.Error_UserNameParse, User.Name, error);
                    throw new ArgumentException(Resources.Error_UserNameParse.FormatInvariant(User.Name, error));
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

                psi.EnvironmentVariables["USERPROFILE"] = Invariant($"{psi.EnvironmentVariables["HOMEDRIVE"]}{psi.EnvironmentVariables["HOMEPATH"]}");
                _sessionLogger.LogTrace(Resources.Trace_EnvironmentVariable, "USERPROFILE", psi.EnvironmentVariables["USERPROFILE"]);

                psi.EnvironmentVariables["APPDATA"] = Invariant($"{psi.EnvironmentVariables["USERPROFILE"]}\\AppData\\Roaming");
                _sessionLogger.LogTrace(Resources.Trace_EnvironmentVariable, "APPDATA", psi.EnvironmentVariables["APPDATA"]);

                psi.EnvironmentVariables["LOCALAPPDATA"] = Invariant($"{psi.EnvironmentVariables["USERPROFILE"]}\\AppData\\Local");
                _sessionLogger.LogTrace(Resources.Trace_EnvironmentVariable, "LOCALAPPDATA", psi.EnvironmentVariables["LOCALAPPDATA"]);

                psi.EnvironmentVariables["TEMP"] = Invariant($"{psi.EnvironmentVariables["LOCALAPPDATA"]}\\Temp");
                _sessionLogger.LogTrace(Resources.Trace_EnvironmentVariable, "TEMP", psi.EnvironmentVariables["TEMP"]);

                psi.EnvironmentVariables["TMP"] = Invariant($"{psi.EnvironmentVariables["LOCALAPPDATA"]}\\Temp");
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
                State = SessionState.Terminated;
                _hostTcs.SetResult(_process.ExitCode);
            };

            lock (_stateLock) {
                if (State != SessionState.Dormant) {
                    throw new InvalidOperationException("Host process is already running");
                }

                _sessionLogger.LogInformation(Resources.Info_StartingRHost, Id, User.Name, rhostExePath, arguments);

                try {
                    _process.Start();
                    _process.WaitForExit(250);
                    if (_process.HasExited && _process.ExitCode < 0) {
                        var message = ErrorCodeConverter.MessageFromErrorCode(_process.ExitCode);
                        throw string.IsNullOrEmpty(message) ? new Win32Exception(_process.ExitCode) : new Win32Exception(message);
                    }
                } catch (Exception ex) {
                    _sessionLogger.LogError(Resources.Error_RHostFailedToStart, ex.Message);
                    throw;
                }

                _sessionLogger.LogInformation(Resources.Info_StartedRHost, Id, User.Name);

                _process.BeginErrorReadLine();

                var hostEnd = _pipe.ConnectHost(_process.Id);
                _hostEnd = hostEnd;

                ClientToHostWorker(_process.StandardInput.BaseStream, hostEnd).DoNotWait();
                HostToClientWorker(_process.StandardOutput.BaseStream, hostEnd).DoNotWait();

                State = SessionState.Running;
            }
        }

        private void KillHost() {
            _sessionLogger.LogTrace("Killing host process for session '{0}'.", Id);

            try {
                _process?.Kill();
            } catch (Exception ex) {
                _sessionLogger.LogError(0, ex, "Failed to kill host process for session '{0}'.", Id);
                throw;
            }

            _process = null;
        }

        public void Kill() {
            State = SessionState.Terminated;
            Task.Run(() => KillHost()).SilenceException<Exception>().DoNotWait();
        }

        public Task TerminateAsync(bool saveRData) {
            var pipe = _hostEnd;
            if (pipe != null) {
                pipe.Write(saveRData ? _saveAndShutdownRequest : _discardAndShutdownRequest);
            }

            return _hostTcs.Task;
        }

        public IMessagePipeEnd ConnectClient() {
            _sessionLogger.LogTrace("Connecting client to message pipe for session '{0}'.", Id);

            if (_pipe == null) {
                _sessionLogger.LogError("Session '{0}' already has a client pipe connected.", Id);
                throw new InvalidOperationException(Resources.Error_RHostFailedToStart.FormatInvariant(Id));
            }

            return _pipe.ConnectClient();
        }

        private async Task ClientToHostWorker(Stream stream, IMessagePipeEnd pipe) {
            using (stream) {
                while (true) {
                    byte[] message;
                    try {
                        message = await pipe.ReadAsync(Program.CancellationToken);
                    } catch (PipeDisconnectedException) {
                        break;
                    }

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
