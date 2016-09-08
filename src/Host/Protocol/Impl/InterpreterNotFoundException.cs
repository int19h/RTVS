// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Runtime.Serialization;

namespace Microsoft.R.Host.Protocol {
    public class InterpreterNotFoundException : Exception, IHasErrorInfo {
        public string InterpreterId { get; }

        public InterpreterNotFoundException(InterpreterNotFoundErrorInfo errorInfo)
            : this(errorInfo.InterpreterId) {
        }

        public InterpreterNotFoundException(string interpreterId, Exception innerException = null)
            : base(string.IsNullOrEmpty(interpreterId) ? "No interpreters available" :  $"No interpreter with ID '{interpreterId}' available", innerException) {
        }

        protected InterpreterNotFoundException(SerializationInfo info, StreamingContext context)
            : base(info, context) {
        }

        public void InspectErrorInfo(IErrorInfoInspector inspector) {
            inspector.Inspect(new InterpreterNotFoundErrorInfo {
                InterpreterId = InterpreterId
            });
        }
    }
}
