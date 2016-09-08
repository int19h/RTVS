// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.R.Host.Protocol;
using Newtonsoft.Json;

namespace Microsoft.R.Host.Broker.Errors {
    public class RestExceptionMiddleware {
        private readonly RequestDelegate _next;

        public RestExceptionMiddleware(RequestDelegate next) {
            _next = next;
        }

        public async Task Invoke(HttpContext context) {
            try {
                await _next.Invoke(context);
            } catch (RestException ex) {
                context.Response.StatusCode = (int)ex.HttpStatusCode;
                context.Response.Headers[RestException.ErrorCodeHeaderName] = ex.ErrorCode.ToString();

                var responseFeature = context.Features.Get<IHttpResponseFeature>();
                responseFeature.ReasonPhrase = ex.Message;

                var errorInfo = (ex as IHasErrorInfo)?.GetErrorInfo();
                if (errorInfo != null) {
                    await context.Response.WriteAsync(JsonConvert.SerializeObject(errorInfo));
                }
            }
        }
    }
}
