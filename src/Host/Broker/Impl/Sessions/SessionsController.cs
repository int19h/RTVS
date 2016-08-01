// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.R.Host.Broker.Interpreters;
using Microsoft.R.Host.Broker.Pipes;
using Microsoft.R.Host.Broker.Security;

namespace Microsoft.R.Host.Broker.Sessions {
    [Authorize(Policy = Policies.RUser)]
    [Route("/sessions")]
    public class SessionsController : Controller {
        private readonly InterpreterManager _interpManager;
        private readonly SessionManager _sessionManager;

        public SessionsController(InterpreterManager interpManager, SessionManager sessionManager) {
            _interpManager = interpManager;
            _sessionManager = sessionManager;
        }

        [HttpGet]
        public IEnumerable<SessionInfo> Get() {
            yield break;
        }

        [HttpPut("{id}")]
        public SessionInfo Put(Guid id, [FromBody] SessionCreateRequest request) {
            var interp = _interpManager.Interpreters.First(ip => ip.Info.Id ==  request.InterpreterId);
            var session = _sessionManager.CreateSession(id, interp, User.Identity, Url);
            return session.Info;
        }

        [HttpGet("{id}/pipe")]
        public Task GetPipe(Guid id, [FromServices] PipeRequestHandler pipes) {
            //var interp = _interpManager.Interpreters.First(ip => ip.Info.Id == request.InterpreterId);
            //var session = _sessionManager.GetSession(id);
            return pipes.HandleRequest(HttpContext, false);
        }
    }
}
