// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;

namespace Microsoft.Common.Core.Shell {
    public interface ITaskDialog {
        string Title { get; set; }
        string MainInstruction { get; set; }
        string Content { get; set; }
        string VerificationText { get; set; }
        string ExpandedInformation { get; set; }
        string Footer { get; set; }

        bool ExpandedByDefault { get; set; }
        bool ShowExpandedInformationInContent { get; set; }
        string ExpandedControlText { get; set; }
        string CollapsedControlText { get; set; }

        int? Width { get; set; }
        bool EnableHyperlinks { get; set; }
        bool AllowCancellation { get; set; }
        bool UseCommandLinks { get; set; }
        bool CanMinimize { get; set; }

        TaskDialogIcon MainIcon { get; set; }
        TaskDialogIcon FooterIcon { get; set; }

        ICollection<TaskDialogButton> Buttons { get; }
        ICollection<TaskDialogButton> RadioButtons { get; }

        TaskDialogButton SelectedButton { get; set; }
        TaskDialogButton SelectedRadioButton { get; set; }
        bool IsVerified { get; set; }

        TaskDialogButton ShowModal();

        /// <summary>
        /// Raised when a hyperlink in the dialog is clicked. If no event
        /// handlers are added, the default behavior is to open an external
        /// browser.
        /// </summary>
        event EventHandler<TaskDialogHyperlinkClickedEventArgs> HyperlinkClicked;
    }
}