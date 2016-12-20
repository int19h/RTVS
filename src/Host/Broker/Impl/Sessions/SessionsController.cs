﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Common.Core.Security;
using Microsoft.R.Host.Broker.Interpreters;
using Microsoft.R.Host.Broker.Pipes;
using Microsoft.R.Host.Broker.Security;
using Microsoft.R.Host.Protocol;

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
        public Task<IEnumerable<SessionInfo>> GetAsync() => Task.FromResult(_sessionManager.GetSessions(User.Identity).Select(s => s.Info));

        [HttpPut("{id}")]
        public IActionResult PutAsync(string id, [FromBody] SessionCreateRequest request) {
            if (!_interpManager.Interpreters.Any()) {
                return new ApiErrorResult(BrokerApiError.NoRInterpreters);
            }

            string profilePath = User.FindFirst(Claims.RUserProfileDir)?.Value;
            var password = User.FindFirst(Claims.Password)?.Value.ToSecureString();

            Interpreter interp;
            if (!string.IsNullOrEmpty(request.InterpreterId)) {
                interp = _interpManager.Interpreters.FirstOrDefault(ip => ip.Id == request.InterpreterId);
                if (interp == null) {
                    return new ApiErrorResult(BrokerApiError.InterpreterNotFound);
                }
            } else {
                interp = _interpManager.Interpreters.First();
            }

            try {
                Session session;
                if (_sessionManager.TryCreateSession(request.ReplaceExisting, User.Identity, id, interp, password, profilePath, request.CommandLineArguments, request.IsTransient, out session)) {
                    return new ObjectResult(session.Info);
                } else {
                    return new ApiErrorResult(BrokerApiError.SessionAlreadyExists);
                }
            } catch (Exception ex) {
                return new ApiErrorResult(BrokerApiError.UnableToStartRHost, ex.Message);
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id, [FromBody] SessionTerminateRequest request) {
            var session = _sessionManager.GetSession(User.Identity, id);
            if (session == null) {
                return NotFound();
            }

            try {
                if (request.IsGraceful) {
                    await session.TerminateAsync(request.SaveRData);
                } else {
                    session.Kill();
                }
            } catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException) {
                return new ApiErrorResult(BrokerApiError.UnableToTerminateRHost, ex.Message);
            } 

            return Ok();
        }

        [HttpGet("{id}/pipe")]
        public IActionResult GetPipe(string id) {
            var session = _sessionManager.GetSession(User.Identity, id);
            if (session?.Process?.HasExited ?? true) {
                return NotFound();
            }

            IMessagePipeEnd pipe;
            try {
                pipe = session.ConnectClient();
            } catch (InvalidOperationException) {
                return new ApiErrorResult(BrokerApiError.PipeAlreadyConnected);
            }

            return new WebSocketPipeAction(id, pipe);
        }
    }
}
