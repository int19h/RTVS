// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Security.Principal;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.R.Host.Broker.Interpreters;

namespace Microsoft.R.Host.Broker.Sessions {
    public class SessionManager {
        private readonly InterpreterManager _interpManager;
        private readonly Dictionary<IIdentity, List<Session>> _sessions = new Dictionary<IIdentity, List<Session>>();

        [ImportingConstructor]
        public SessionManager(InterpreterManager interpManager) {
            _interpManager = interpManager;
        }

        public IEnumerable<Session> GetSessions(IIdentity user) {
            lock (_sessions) {
                List<Session> userSessions;
                _sessions.TryGetValue(user, out userSessions);
                return userSessions ?? Enumerable.Empty<Session>();
            }
        }

        public Session GetSession(Guid id) {
            return _sessions.Values.SelectMany(sessions => sessions).FirstOrDefault(session => session.Id == id);
        }

        public Session CreateSession(Guid id, Interpreter interpreter, IIdentity user, IUrlHelper urlHelper) {
            Session session;

            lock (_sessions) {
                List<Session> userSessions;
                _sessions.TryGetValue(user, out userSessions);
                if (userSessions == null) {
                    _sessions[user] = userSessions = new List<Session>();
                }

                session = new Session(this, id, interpreter, user);
                userSessions.Add(session);
            }

            session.Start(urlHelper);
            return session;
        }
    }
}
