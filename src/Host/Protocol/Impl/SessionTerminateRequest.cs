// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Microsoft.R.Host.Protocol {
    public struct SessionTerminateRequest {
        /// <summary>
        /// If <see langword="true"/>, the host process is requested to terminate itself gracefully.
        /// Otherwise, the host process is killed without notification.
        /// </summary>
        public bool IsGraceful { get; set; }

        /// <summary>
        /// If <see langword="true"/>, the host process is requested to save its current state
        /// to an RData file, so that it may be reloaded later.
        /// </summary>
        /// <remarks>
        /// This property is only applicable when <see cref="IsGraceful"/> is <see langword="true"/>,
        /// and is ignored otherwise.
        /// </remarks>
        public bool SaveRData { get; set; }
    }
}
