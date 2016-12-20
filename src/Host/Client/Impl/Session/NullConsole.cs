﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Common.Core.Shell;

namespace Microsoft.R.Host.Client.Session {
    internal class NullConsole : IConsole {
        public void Write(string text) {}
        public void WriteLine(string text) { }
        public Task<bool> PromptYesNoAsync(string text, CancellationToken cancellationToken) => Task.FromResult(true);
        public ITaskDialogProvider TaskDialogs => null;
    }
}