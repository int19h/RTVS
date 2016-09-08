// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Runtime.Serialization;

namespace Microsoft.R.Host.Protocol {
    public static class HasErrorInfoExtensions {
        private class ErrorInfoInspector : IErrorInfoInspector {
            public object ErrorInfo;

            public void Inspect<TErrorInfo>(TErrorInfo errorInfo) {
                ErrorInfo = errorInfo;
            }
        }

        public static object GetErrorInfo(this IHasErrorInfo hasErrorInfo) {
            var inspector = new ErrorInfoInspector();
            hasErrorInfo.InspectErrorInfo(inspector);
            return inspector.ErrorInfo;
        }
    }
}
