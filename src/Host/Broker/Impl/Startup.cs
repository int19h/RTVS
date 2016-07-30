// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebSockets.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.R.Host.Broker.Interpreters;
using Microsoft.R.Host.Broker.Pipes;
using Microsoft.R.Host.Broker.Security;
using Microsoft.R.Host.Broker.Sessions;

namespace Microsoft.R.Host.Broker {
    public class Startup {
        public Startup(IHostingEnvironment env) {
        }

        public void ConfigureServices(IServiceCollection services) {
            services.AddOptions()
                .Configure<InterpretersOptions>(Program.Configuration.GetSection("Interpreters"))
                .Configure<SecurityOptions>(Program.Configuration.GetSection("Security"));

            services.AddSingleton<InterpreterManager>();

            services.AddSingleton<SessionManager>();

            services.AddSingleton<IAuthorizationHandler, RUserAuthorizationHandler>();

            services.AddAuthorization(options => options.AddPolicy(Policies.RUser, policy =>
                policy.AddRequirements(new RUserAuthorizationRequirement())));

            services.AddRouting();

            services.AddSingleton<PipeRequestHandler>();

            services.AddMvc();
        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory, PipeRequestHandler pipeRequestHandler) {
            loggerFactory.AddConsole(LogLevel.Trace);
            loggerFactory.AddDebug();

            app.UseWebSockets(new WebSocketOptions {
                ReplaceFeature = true,
                KeepAliveInterval = TimeSpan.FromMilliseconds(1000000000),
                ReceiveBufferSize = 0x10000
            });

            app.UseMvc();

            var routeBuilder = new RouteBuilder(app)
                .MapGet("pipes/{id:guid}", pipeRequestHandler.HandleRequest);
            app.UseRouter(routeBuilder.Build());
        }
    }
}
