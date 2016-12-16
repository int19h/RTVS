// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Common.Core.Shell;

namespace Microsoft.R.Host.Client {
    public interface IConsole {
        ITaskDialogProvider TaskDialogs { get; }
        void Write(string text);
        void WriteLine(string text);
        Task<bool> PromptYesNoAsync(string text, CancellationToken cancellationToken);
    }
}