// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.R.Host.Broker.Security;

namespace Microsoft.R.Host.Broker.Sessions {
    [Authorize(Policy = Policies.RUser)]
    [Route("/sessions")]
    public class SessionsController : Controller {
        public SessionsController() {
        }

        [HttpGet]
        public IEnumerable<SessionInfo> Get() {
            yield break;
        }
    }
}
