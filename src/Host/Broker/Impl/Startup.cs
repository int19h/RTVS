// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.R.Host.Broker.Interpreters;
using Microsoft.R.Host.Broker.Lifetime;
using Microsoft.R.Host.Broker.Logging;
using Microsoft.R.Host.Broker.Security;
using Microsoft.R.Host.Broker.Sessions;

namespace Microsoft.R.Host.Broker {
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

        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory, LifetimeManager lifetimeManager, InterpreterManager interpreterManager) {
            loggerFactory
                .AddDebug()
                .AddConsole(LogLevel.Trace)
                .AddProvider(new FileLoggerProvider());

            lifetimeManager.Initialize();
            interpreterManager.Initialize();

            app.UseWebSockets(new WebSocketOptions {
                ReplaceFeature = true,
                KeepAliveInterval = TimeSpan.FromMilliseconds(1000000000),
                ReceiveBufferSize = 0x10000
            });

            app.UseMvc();
        }
    }
}
