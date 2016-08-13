// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Odachi.AspNetCore.Authentication.Basic;

namespace Microsoft.R.Host.Broker.Security {
    public class SecurityManager {
        private readonly SecurityOptions _options;
        private readonly ILogger _logger;

        private CancellationTokenSource _cts;

        public SecurityManager(IOptions<SecurityOptions> options, ILogger<SecurityManager> logger) {
            _options = options.Value;
            _logger = logger;
        }

        public Task SignInAsync(BasicSignInContext context) {
            IIdentity identity = (_options.Secret != null) ? SignInUsingSecret(context) : SignInUsingLogon(context);
            if (identity != null) {
                var principal = new ClaimsPrincipal(identity);
                context.Ticket = new AuthenticationTicket(principal, new AuthenticationProperties(), context.Options.AuthenticationScheme);
            }

            context.HandleResponse();
            return Task.CompletedTask;
        }

        private IIdentity SignInUsingSecret(BasicSignInContext context) {
            if (_options.Secret != context.Password) {
                return null;
            }

            var claims = new[] {
                new Claim(ClaimTypes.Name, context.Username),
                new Claim(ClaimTypes.Role, _options.AllowedGroup)
            };

            return new ClaimsIdentity(claims, context.Options.AuthenticationScheme);
        }

        private IIdentity SignInUsingLogon(BasicSignInContext context) {
            IntPtr token;
            if (NativeMethods.LogonUser(context.Username, null, context.Password, NativeMethods.LOGON32_LOGON_NETWORK, NativeMethods.LOGON32_PROVIDER_DEFAULT, out token)) {
                return new WindowsIdentity(token);
            } else {
                return null;
            }
        }
    }
}
