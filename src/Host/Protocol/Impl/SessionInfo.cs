// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Microsoft.R.Host.Protocol {
    public class SessionInfo {
        public string Id { get; set; }

        public string InterpreterId { get; set; }

        public string CommandLineArguments { get; set; }

        /// <remarks>
        /// <para>
        /// A session is transient if it does not contain any valuable data that cannot be easily recreated.
        /// For example, RTVS Intellisense and package management sessions are transient, while the REPL
        /// session is not, because it may contain variables and other state created by the user.
        /// </para>
        /// <para>
        /// Thus, a transient session can be destroyed and re-created freely if necessary, and no confirmation
        /// or attempt to save session state is required.
        /// </para>
        /// </remarks>
        public bool IsTransient { get; set; } = true;

        public SessionState State { get; set; }
    }
}
