// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Microsoft.R.Host.Protocol {
    public class RestException : Exception {
        public const string ErrorCodeHeaderName = "X-Microsoft.R.Host.Protocol-ErrorCode";

        public HttpStatusCode HttpStatusCode { get; }

        /// <summary>
        /// Error code that further clarifies <see cref="HttpStatusCode"/>. Unlike the latter, the set of values here
        /// is distinct for each REST API endpoint. 
        /// </summary>
        public int ErrorCode { get; }

        public RestException(string message, HttpStatusCode httpStatusCode, int errorCode, Exception innerException = null)
            : base(message, innerException) {

            int rawStatus = (int)httpStatusCode;
            if (rawStatus < 400 || rawStatus > 499) {
                throw new ArgumentException($"HTTP status code for {nameof(RestException)} must be in range 400-499", nameof(httpStatusCode));
            }

            HttpStatusCode = httpStatusCode;
            ErrorCode = errorCode;
        }
    }

    public class RestException<TErrorInfo> : RestException, IHasErrorInfo {
        public TErrorInfo ErrorInfo { get; }

        public RestException(string message, HttpStatusCode httpStatusCode, int errorCode, TErrorInfo errorInfo, Exception innerException = null)
            : base(message, httpStatusCode, errorCode, innerException) {
            ErrorInfo = errorInfo;
        }

        public void InspectErrorInfo(IErrorInfoInspector inspector) {
            inspector.Inspect(ErrorInfo);
        }
    }
}
