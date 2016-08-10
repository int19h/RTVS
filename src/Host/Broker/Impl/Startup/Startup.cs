// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.R.Host.Broker.Interpreters;
using Microsoft.R.Host.Broker.Lifetime;
using Microsoft.R.Host.Broker.Logging;
using Microsoft.R.Host.Broker.Security;
using Microsoft.R.Host.Broker.Sessions;
using Odachi.AspNetCore.Authentication.Basic;

namespace Microsoft.R.Host.Broker.Startup {
    public class Startup {
        public Startup(IHostingEnvironment env) {
        }

        public void ConfigureServices(IServiceCollection services) {
            services.AddOptions()
                .Configure<LoggingOptions>(Program.Configuration.GetSection("logging"))
                .Configure<LifetimeOptions>(Program.Configuration.GetSection("lifetime"))
                .Configure<SecurityOptions>(Program.Configuration.GetSection("security"))
                .Configure<ROptions>(Program.Configuration.GetSection("R"));

            services.AddSingleton<LifetimeManager>();

            services.AddSingleton<InterpreterManager>();

            services.AddSingleton<SessionManager>();

            services.AddSingleton<IAuthorizationHandler, RUserAuthorizationHandler>();

            services.AddAuthorization(options => options.AddPolicy(Policies.RUser, policy =>
                policy.AddRequirements(new RUserAuthorizationRequirement())));

            services.AddRouting();

            services.AddMvc();
        }

        public void Configure(
            IApplicationBuilder app,
            IHostingEnvironment env,
            LifetimeManager lifetimeManager,
            InterpreterManager interpreterManager
        ) {
            lifetimeManager.Initialize();
            interpreterManager.Initialize();

            app.UseWebSockets(new WebSocketOptions {
                ReplaceFeature = true,
                KeepAliveInterval = TimeSpan.FromMilliseconds(1000000000),
                ReceiveBufferSize = 0x10000
            });

            app.UseBasicAuthentication(options => {
                options.Events = new BasicEvents { OnSignIn = SignIn };
            });

            app.Use((context, next) => {
                if (!context.User.Identity.IsAuthenticated) {
                    return context.Authentication.ChallengeAsync();
                } else {
                    return next();
                }
            });

            app.UseMvc();
        }

        private Task SignIn(BasicSignInContext context) {
            var securityOptions = context.HttpContext.RequestServices.GetRequiredService<IOptions<SecurityOptions>>().Value;

            if (securityOptions.Secret != null && securityOptions.Secret == context.Password) {
                var claims = new[] {
                                new Claim(ClaimTypes.Name, context.Username),
                                new Claim(ClaimTypes.Role, securityOptions.AllowedGroup)
                            };

                var identity = new ClaimsIdentity(claims, context.Options.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);
                context.Ticket = new AuthenticationTicket(principal, new AuthenticationProperties(), context.Options.AuthenticationScheme);

                context.HandleResponse();
            }

            return Task.CompletedTask;
        }
    }
}
