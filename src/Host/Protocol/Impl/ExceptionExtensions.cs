// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Diagnostics;
using System.Net;
using System.Reflection;

namespace Microsoft.R.Host.Protocol {
    public static class ExceptionExtensions {
        private class TypedRestExceptionBuilder : IErrorInfoInspector {
            private readonly Exception _exception;
            private readonly HttpStatusCode _httpStatusCode;
            private readonly int _errorCode;

            public RestException Exception { get; private set; }

            public TypedRestExceptionBuilder(Exception exception, HttpStatusCode httpStatusCode, int errorCode) {
                _exception = exception;
                _httpStatusCode = httpStatusCode;
                _errorCode = errorCode;
            }

            public void Inspect<TErrorInfo>(TErrorInfo errorInfo) {
                Exception = new RestException<TErrorInfo>(_exception.Message, _httpStatusCode, _errorCode, errorInfo, _exception);
            }
        }

        public static RestException ToRestException(this Exception ex, HttpStatusCode httpStatusCode, int errorCode) {
            var hasErrorInfo = ex as IHasErrorInfo;
            if (hasErrorInfo == null) {
                return new RestException(ex.Message, httpStatusCode, errorCode, ex);
            }

            var exceptionBuilder = new TypedRestExceptionBuilder(ex, httpStatusCode, errorCode);
            hasErrorInfo.InspectErrorInfo(exceptionBuilder);
            return exceptionBuilder.Exception;
        }

        public static RestException ToRestException<TErrorCode>(this Exception ex, HttpStatusCode httpStatusCode, TErrorCode errorCode) {
            var errorCodeType = typeof(TErrorCode);
            if (!errorCodeType.IsEnum) {
                throw new ArgumentException($"{errorCodeType.Name} is not an enum type", nameof(errorCode));
            }

            var restEx = ex.ToRestException(httpStatusCode, ((IConvertible)errorCode).ToInt32(null));

            var errorInfo = (restEx as IHasErrorInfo)?.GetErrorInfo();
            Type expectedType = null;

            string fieldName = Enum.GetName(errorCodeType, errorCode);
            if (fieldName != null) {
                var field = errorCodeType.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
                var errorInfoAttr = field?.GetCustomAttribute<ErrorInfoAttribute>();
                expectedType = errorInfoAttr?.GetType();
            }

            Trace.Assert(expectedType?.IsAssignableFrom(errorInfo?.GetType()) ?? true);

            return restEx;
        }
    }
}
