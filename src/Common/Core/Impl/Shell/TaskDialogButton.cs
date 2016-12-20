// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;

namespace Microsoft.Common.Core.Shell {
    public class TaskDialogButton {
        public TaskDialogButton(string text) {
            int i = text.IndexOfAny(Environment.NewLine.ToCharArray());
            if (i < 0) {
                Text = text;
            } else {
                Text = text.Remove(i);
                Subtext = text.Substring(i).TrimStart();
            }
        }

        public TaskDialogButton(string text, string subtext) {
            Text = text;
            Subtext = subtext;
        }

        public string Text { get; set; }
        public string Subtext { get; set; }
        public bool ElevationRequired { get; set; }

        private TaskDialogButton() { }

        public static readonly TaskDialogButton OK = new TaskDialogButton();
        public static readonly TaskDialogButton Cancel = new TaskDialogButton();
        public static readonly TaskDialogButton Yes = new TaskDialogButton();
        public static readonly TaskDialogButton No = new TaskDialogButton();
        public static readonly TaskDialogButton Retry = new TaskDialogButton();
        public static readonly TaskDialogButton Close = new TaskDialogButton();
    }
}