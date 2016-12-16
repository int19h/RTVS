// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Common.Core.Shell;

namespace Microsoft.Common.Core.Test.Fakes.Shell {
    [ExcludeFromCodeCoverage]
    public class TestTaskDialog : ITaskDialog {
        public bool AllowCancellation { get; set; }
        public ICollection<TaskDialogButton> Buttons { get; set; }
        public bool CanMinimize { get; set; }
        public string CollapsedControlText { get; set; }
        public string Content { get; set; }
        public bool EnableHyperlinks { get; set; }
        public bool ExpandedByDefault { get; set; }
        public string ExpandedControlText { get; set; }
        public string ExpandedInformation { get; set; }
        public string Footer { get; set; }
        public TaskDialogIcon FooterIcon { get; set; }
        public bool IsVerified { get; set; }
        public TaskDialogIcon MainIcon { get; set; }
        public string MainInstruction { get; set; }
        public ICollection<TaskDialogButton> RadioButtons { get; set; }
        public TaskDialogButton SelectedButton { get; set; }
        public TaskDialogButton SelectedRadioButton { get; set; }
        public bool ShowExpandedInformationInContent { get; set; }
        public string Title { get; set; }
        public bool UseCommandLinks { get; set; }
        public string VerificationText { get; set; }
        public int? Width { get; set; }

        public event EventHandler<TaskDialogHyperlinkClickedEventArgs> HyperlinkClicked;

        public TaskDialogButton ShowModal() => SelectedButton;

        public Task<TaskDialogButton> ShowModalAsync() => Task.FromResult(SelectedButton);
    }
}