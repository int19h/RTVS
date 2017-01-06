// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace Microsoft.R.Host.Client.Host {
    public class HostConnectionInfo {
        private static readonly IRCallbacks _nullCallbacks = new NullRCallbacks();

        public string Name { get; }
        public IRCallbacks Callbacks { get; }
        public int Timeout { get; }
        public bool UseRHostCommandLineArguments { get; }

        /// <seealso cref="IRSession.IsTransient"/>
        public bool IsTransient { get; }

        public HostConnectionInfo(string name, bool isTransient, IRCallbacks callbacks, bool useRHostCommandLineArguments = false, int timeout = 3000) {
            Name = name;
            Callbacks = callbacks ?? _nullCallbacks;
            UseRHostCommandLineArguments = useRHostCommandLineArguments;
            Timeout = timeout;
            IsTransient = isTransient;
        }
    }
}