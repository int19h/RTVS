// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Microsoft.R.Host.Protocol {
    public struct SessionCreateRequest {
        public bool ReplaceExisting { get; set; }

        public string InterpreterId { get; set; }

        public string CommandLineArguments { get; set; }

        /// <seealso cref="SessionInfo.IsTransient"/>
        public bool IsTransient { get; set; }
    }
}
