// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Microsoft.R.Host.Broker.Security {
    public class RUserAuthorizationHandler : AuthorizationHandler<RUserAuthorizationRequirement> {
        private readonly SecurityOptions _securityOptions;

        public RUserAuthorizationHandler(IOptions<SecurityOptions> securityOptions) {
            _securityOptions = securityOptions.Value;
        }

        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, RUserAuthorizationRequirement requirement) {
            // If NTLM was used, only allow the same user as the one running the broker (used for local scenarios).
            //var winUser = context.User.Identity as WindowsIdentity;
            //if (winUser != null) {
            //    if (winUser.User == WindowsIdentity.GetCurrent().User) {
            //        context.Succeed(requirement);
            //    }
            //    return Task.CompletedTask;
            //}

            if (context.User.IsInRole(_securityOptions.AllowedGroup)) {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }

            return Task.CompletedTask;
        }
    }
}
