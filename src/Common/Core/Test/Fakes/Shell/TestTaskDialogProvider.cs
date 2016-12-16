// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Common.Core.Shell;

namespace Microsoft.Common.Core.Test.Fakes.Shell {
    [ExcludeFromCodeCoverage]
    public class TestTaskDialogProvider : ITaskDialogProvider {
        public ITaskDialog CreateTaskDialog() => new TestTaskDialog();
    }
}