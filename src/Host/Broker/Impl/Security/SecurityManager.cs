﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Security.Claims;
using System.Security.Principal;
using System.Text;
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
            ClaimsPrincipal principal = (_options.Secret != null) ? SignInUsingSecret(context) : SignInUsingLogon(context);
            if (principal != null) {
                context.Ticket = new AuthenticationTicket(principal, new AuthenticationProperties(), context.Options.AuthenticationScheme);
            }

            context.HandleResponse();
            return Task.CompletedTask;
        }

        private ClaimsPrincipal SignInUsingSecret(BasicSignInContext context) {
            if (_options.Secret != context.Password) {
                return null;
            }

            var claims = new[] {
                new Claim(ClaimTypes.Name, context.Username),
                new Claim(Claims.RUser, "")
            };

            var identity = new ClaimsIdentity(claims, context.Options.AuthenticationScheme);
            return new ClaimsPrincipal(identity);
        }

        private ClaimsPrincipal SignInUsingLogon(BasicSignInContext context) {
            var user = new StringBuilder(NativeMethods.CREDUI_MAX_USERNAME_LENGTH + 1);
            var domain = new StringBuilder(NativeMethods.CREDUI_MAX_PASSWORD_LENGTH + 1);

            if (NativeMethods.CredUIParseUserName(context.Username, user, user.Capacity, domain, domain.Capacity) != 0) {
                return null;
            }

            IntPtr token;
            if (!NativeMethods.LogonUser(user.ToString(), domain.ToString(), context.Password, NativeMethods.LOGON32_LOGON_NETWORK, NativeMethods.LOGON32_PROVIDER_DEFAULT, out token)) {
                return null;
            }

            var winIdentity = new WindowsIdentity(token);
            var principal = new WindowsPrincipal(winIdentity);

            if (principal.IsInRole(_options.AllowedGroup)) {
                var claims = new[] {
                    new Claim(ClaimTypes.Name, context.Username),
                    new Claim(Claims.RUser, "")
                };

                var claimsIdentity = new ClaimsIdentity(claims, context.Options.AuthenticationScheme);
                principal.AddIdentities(new[] { claimsIdentity });
            }

            return principal;
        }
    }
}
