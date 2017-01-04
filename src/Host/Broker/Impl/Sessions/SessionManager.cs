﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Security;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.Common.Core;
using Microsoft.Common.Core.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.R.Host.Broker.Interpreters;
using Microsoft.R.Host.Broker.Logging;
using Microsoft.R.Host.Broker.Pipes;
using Microsoft.R.Host.Protocol;

namespace Microsoft.R.Host.Broker.Sessions {
    public class SessionManager {
        private const int MaximumConcurrentClientWindowsUsers = 1;

        private readonly InterpreterManager _interpManager;
        private readonly LoggingOptions _loggingOptions;
        private readonly ILogger _hostOutputLogger, _messageLogger, _sessionLogger;

        private readonly Dictionary<string, List<Session>> _sessions = new Dictionary<string, List<Session>>();
        private readonly HashSet<string> _blockedUsers = new HashSet<string>();

        [ImportingConstructor]
        public SessionManager(
            InterpreterManager interpManager,
            IOptions<LoggingOptions> loggingOptions,
            ILogger<Session> sessionLogger,
            ILogger<MessagePipe> messageLogger,
            ILogger<Process> hostOutputLogger
        ) {
            _interpManager = interpManager;
            _loggingOptions = loggingOptions.Value;
            _sessionLogger = sessionLogger;

            if (_loggingOptions.LogPackets) {
                _messageLogger = messageLogger;
            }

            if (_loggingOptions.LogHostOutput) {
                _hostOutputLogger = hostOutputLogger;
            }
        }

        public IEnumerable<Session> GetSessions(IIdentity user) {
            lock (_sessions) {
                List<Session> userSessions;
                if(_sessions.TryGetValue(user.Name, out userSessions)) {
                    return userSessions.ToArray();
                }
                return Enumerable.Empty<Session>();
            }
        }

        public IDisposable BlockSessionsCreationForUser(IIdentity user, bool terminateSession) {
            lock (_sessions) {
                if (terminateSession) {
                    var userSessions = GetOrCreateSessionList(user);
                    var sessions = userSessions.ToArray();
                    foreach (var session in sessions) {
                        userSessions.Remove(session);
                        session.Kill();
                    }
                }
                _blockedUsers.Add(user.Name);

                return new UserSessionCreationBlocker(this, user);
            }
        }

        private void UnblockSessionCreationForUser(IIdentity user) {
            lock (_sessions) {
                _blockedUsers.Remove(user.Name);
            }
        }

        public IEnumerable<string> GetUsers() {
            lock (_sessions) {
                return _sessions.Keys.ToArray();
            }
        }

        public Session GetSession(IIdentity user, string id) {
            lock (_sessions) {
                if (_blockedUsers.Contains(user.Name)) {
                    return null;
                }

                return _sessions.Values.SelectMany(sessions => sessions).FirstOrDefault(session => session.User.Name == user.Name && session.Id == id);
            }
        }

        private List<Session> GetOrCreateSessionList(IIdentity user) {
            lock (_sessions) {
                List<Session> userSessions;
                _sessions.TryGetValue(user.Name, out userSessions);
                if (userSessions == null) {
                    _sessions[user.Name] = userSessions = new List<Session>();
                }

                return userSessions;
            }
        }

        public bool TryCreateSession(
            bool replaceExisting,
            IIdentity user,
            string id,
            Interpreter interpreter,
            SecureString password,
            string profilePath,
            string commandLineArguments,
            bool isTransient,
            out Session session
        ) {
            lock (_sessions) {
                if (_blockedUsers.Contains(user.Name)) {
                    throw new InvalidOperationException(Resources.Error_BlockedByProfileDeletion.FormatInvariant(user.Name));
                }

                var userSessions = GetOrCreateSessionList(user);

                session = userSessions.SingleOrDefault(s => s.Id == id);
                if (session != null) {
                    if (replaceExisting) {
                        session.Kill();
                    } else {
                        return false;
                    }
                }

                session = new Session(this, user, id, interpreter, commandLineArguments, isTransient, _sessionLogger, _messageLogger);
                session.StateChanged += Session_StateChanged;

                userSessions.Add(session);
            }

            session.StartHost(
                password,
                profilePath,
                _loggingOptions.LogHostOutput ? _hostOutputLogger : null,
                _loggingOptions.LogPackets || _loggingOptions.LogHostOutput ? LogVerbosity.Traffic : LogVerbosity.Minimal);
            return true;
        }

        private void Session_StateChanged(object sender, SessionStateChangedEventArgs e) {
            var session = (Session)sender;
            if (e.NewState == SessionState.Terminated) {
                lock (_sessions) {
                    var userSessions = GetOrCreateSessionList(session.User);
                    userSessions.Remove(session);

                    if (userSessions.Count == 0) {
                        _sessions.Remove(session.User.Name);
                    }
                }
            }
        }

        private class UserSessionCreationBlocker : IDisposable {
            private readonly SessionManager _sessionManager;
            private readonly IIdentity _user;
            public UserSessionCreationBlocker(SessionManager sessionManager, IIdentity user) {
                _sessionManager = sessionManager;
                _user = user;
            }

            public void Dispose() {
                _sessionManager.UnblockSessionCreationForUser(_user);
            }
        }
    }
}
