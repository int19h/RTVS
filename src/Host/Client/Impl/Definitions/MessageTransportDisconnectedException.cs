// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;

namespace Microsoft.R.Host.Client {
    [Serializable]
    internal class MessageTransportDisconnectedException : MessageTransportException {
        public MessageTransportDisconnectedException() {
        }

        public MessageTransportDisconnectedException(string message)
            : base(message) {
        }

        public MessageTransportDisconnectedException(string message, Exception innerException)
            : base(message, innerException) {
        }

        public MessageTransportDisconnectedException(Exception innerException)
            : this(innerException.Message, innerException) {
        }
    }
}
