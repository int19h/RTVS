// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.Common.Core.Shell;
using Microsoft.VisualStudio.R.Packages.R;

namespace Microsoft.VisualStudio.R.Package.Shell {
    internal class VsTaskDialogProvider : ITaskDialogProvider {
        private readonly IApplicationShell _shell;

        public VsTaskDialogProvider(IApplicationShell shell) {
            _shell = shell;
        }

        public ITaskDialog CreateTaskDialog() => new VsTaskDialog(RPackage.Current, _shell);
    }           
}
