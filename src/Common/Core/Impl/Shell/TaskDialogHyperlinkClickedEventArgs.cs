// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;

namespace Microsoft.Common.Core.Shell {
    public sealed class TaskDialogHyperlinkClickedEventArgs : EventArgs {
        public string Url { get; }

        public TaskDialogHyperlinkClickedEventArgs(string url) {
            Url = url;
        }
    }
}