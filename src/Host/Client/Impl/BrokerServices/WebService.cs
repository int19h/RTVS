// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.R.Host.Protocol;
using Newtonsoft.Json;

namespace Microsoft.R.Host.Client.BrokerServices {
    public class WebService {
        private interface IRestExceptionBuilder {
            string Message { get; set; }
            HttpStatusCode HttpStatusCode { get; set; }
            int ErrorCode { get; set; }
            object ErrorInfo { get; set; }
            RestException Build();
        }

        private class RestExceptionBuilder<TErrorInfo> : IRestExceptionBuilder {
            public string Message { get; set; }
            public HttpStatusCode HttpStatusCode { get; set; }
            public int ErrorCode { get; set; }
            public object ErrorInfo { get; set; }

            public RestException Build() {
                return new RestException<TErrorInfo>(Message, HttpStatusCode, ErrorCode, (TErrorInfo)ErrorInfo);
            }
        }

        private enum DummyErrorCode { }

        protected HttpClient HttpClient { get; }

        public WebService(HttpClient httpClient) {
            HttpClient = httpClient;
        }

        public async Task<TResponse> HttpGetAsync<TResponse>(Uri uri) =>
            JsonConvert.DeserializeObject<TResponse>(await HttpClient.GetStringAsync(uri));

        public Task<TResponse> HttpGetAsync<TResponse>(UriTemplate uriTemplate, params object[] args) =>
           HttpGetAsync<TResponse>(MakeUri(uriTemplate, args));

        public async Task<TResponse> HttpPutAsync<TRequest, TResponse, TErrorCode>(Uri uri, TRequest request) {
            var requestBody = JsonConvert.SerializeObject(request);
            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            var response = await HttpClient.PutAsync(uri, content);
            await ThrowOnErrorAsync<TErrorCode>(response);

            var responseBody = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<TResponse>(responseBody);
        }

        public Task<TResponse> HttpPutAsync<TRequest, TResponse>(Uri uri, TRequest request) =>
            HttpPutAsync<TRequest, TResponse, DummyErrorCode>(uri, request);

        public Task<TResponse> HttpPutAsync<TRequest, TResponse, TErrorCode>(UriTemplate uriTemplate, TRequest request, params object[] args) =>
            HttpPutAsync<TRequest, TResponse, TErrorCode>(MakeUri(uriTemplate, args), request);

        public Task<TResponse> HttpPutAsync<TRequest, TResponse>(UriTemplate uriTemplate, TRequest request, params object[] args) =>
            HttpPutAsync<TRequest, TResponse, DummyErrorCode>(uriTemplate, request, args);

        private Uri MakeUri(UriTemplate uriTemplate, params object[] args) =>
            uriTemplate.BindByPosition(HttpClient.BaseAddress, args.Select(x => x.ToString()).ToArray());

        private static async Task ThrowOnErrorAsync<TErrorCode>(HttpResponseMessage response) {
            var errorCodeType = typeof(TErrorCode);
            if (!errorCodeType.IsEnum) {
                throw new ArgumentException($"{errorCodeType.Name} is not an enum type", nameof(TErrorCode));
            }

            if (response.IsSuccessStatusCode) {
                return;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden) {
                throw new UnauthorizedAccessException(response.ReasonPhrase);
            }

            int errorCode;
            if (int.TryParse(response.Headers.GetValues(RestException.ErrorCodeHeaderName).LastOrDefault(), out errorCode)) {
                string fieldName = Enum.GetName(errorCodeType, errorCode);
                if (fieldName != null) {
                    var field = errorCodeType.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
                    var errorInfoAttr = field?.GetCustomAttribute<ErrorInfoAttribute>();
                    var errorInfoType = errorInfoAttr?.GetType();

                    if (errorInfoType != null) {
                        string json = await response.Content.ReadAsStringAsync();

                        object errorInfo;
                        try {
                            errorInfo = JsonConvert.DeserializeObject(json, errorInfoType);
                        } catch (JsonException) {
                            errorInfo = null;
                        }

                        if (errorInfo != null) {
                            var builder = (IRestExceptionBuilder)Activator.CreateInstance(typeof(RestExceptionBuilder<>).MakeGenericType(errorInfoType));
                            builder.Message = response.ReasonPhrase;
                            builder.HttpStatusCode = response.StatusCode;
                            builder.ErrorCode = errorCode;
                            builder.ErrorInfo = errorInfo;
                            throw builder.Build();
                        }
                    }
                }

                throw new RestException(response.ReasonPhrase, response.StatusCode, errorCode);
            }

            response.EnsureSuccessStatusCode();
        }
    }
}
