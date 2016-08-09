﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.R.Host.Broker.Interpreters;
using Microsoft.R.Host.Broker.Logging;
using Microsoft.R.Host.Broker.Pipes;

namespace Microsoft.R.Host.Broker.Sessions {
    public class SessionManager {
        private readonly InterpreterManager _interpManager;
        private readonly LoggingOptions _loggingOptions;
        private readonly ILogger _hostOutputLogger, _messageLogger;

        private readonly Dictionary<string, List<Session>> _sessions = new Dictionary<string, List<Session>>();

        [ImportingConstructor]
        public SessionManager(InterpreterManager interpManager, IOptions<LoggingOptions> loggingOptions, ILogger<Process> hostOutputLogger, ILogger<MessagePipe> messageLogger) {
            _interpManager = interpManager;
            _loggingOptions = loggingOptions.Value;
            _hostOutputLogger = hostOutputLogger;
            _messageLogger = messageLogger;
        }

        public IEnumerable<Session> GetSessions(IIdentity user) {
            lock (_sessions) {
                List<Session> userSessions;
                _sessions.TryGetValue(user.Name, out userSessions);
                return userSessions ?? Enumerable.Empty<Session>();
            }
        }

        public Session GetSession(IIdentity user, string id) {
            lock (_sessions) {
                return _sessions.Values.SelectMany(sessions => sessions).FirstOrDefault(session => session.User.Name == user.Name && session.Id == id);
            }
        }

        public Session CreateSession(string id, Interpreter interpreter, IIdentity user, IUrlHelper urlHelper) {
            Session session;

            lock (_sessions) {
                List<Session> userSessions;
                _sessions.TryGetValue(user.Name, out userSessions);
                if (userSessions == null) {
                    _sessions[user.Name] = userSessions = new List<Session>();
                }

                var oldSession = userSessions.FirstOrDefault(s => s.Id == id);
                if (oldSession != null) {
                    oldSession.KillHost();
                    userSessions.Remove(oldSession);
                }

                session = new Session(this, id, interpreter, user);
                userSessions.Add(session);
            }

            session.StartHost(
                _loggingOptions.LogHostOutput ? _hostOutputLogger : null,
                _loggingOptions.LogPackets ? _messageLogger : null);
            return session;
        }
    }
}
