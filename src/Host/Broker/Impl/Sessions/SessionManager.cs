// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Security.Principal;
using Microsoft.AspNetCore.Mvc;
using Microsoft.R.Host.Broker.Interpreters;

namespace Microsoft.R.Host.Broker.Sessions {
    public class SessionManager {
        private readonly InterpreterManager _interpManager;
        private readonly Dictionary<string, List<Session>> _sessions = new Dictionary<string, List<Session>>();

        [ImportingConstructor]
        public SessionManager(InterpreterManager interpManager) {
            _interpManager = interpManager;
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

            session.StartHost();
            return session;
        }
    }
}
