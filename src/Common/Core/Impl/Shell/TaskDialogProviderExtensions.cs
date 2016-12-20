// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Diagnostics;

namespace Microsoft.Common.Core.Shell {
    public static class TaskDialogProviderExtensions {
        public static ITaskDialog CreateTaskDialogFromException(
            this ITaskDialogProvider dialogProvider,
            Exception exception,
            string message = null,
            string issueTrackerUrl = null
        ) {
            string suffix = string.IsNullOrEmpty(issueTrackerUrl) ?
                "Please press Ctrl+C to copy the contents of this dialog and report this error." :
                "Please press Ctrl+C to copy the contents of this dialog and report this error to our <a href=\"issuetracker\">issue tracker</a>.";

            if (string.IsNullOrEmpty(message)) {
                message = suffix;
            } else {
                message += Environment.NewLine + Environment.NewLine + suffix;
            }

            var td = dialogProvider.CreateTaskDialog();
            td.MainInstruction = "An unexpected error occurred";
            td.Content = message;
            td.EnableHyperlinks = true;
            td.CollapsedControlText = "Show &details";
            td.ExpandedControlText = "Hide &details";
            td.ExpandedInformation = exception.ToString();
            td.Buttons.Add(TaskDialogButton.Close);

            if (!string.IsNullOrEmpty(issueTrackerUrl)) {
                td.HyperlinkClicked += (s, e) => {
                    if (e.Url == "issuetracker") {
                        Process.Start(issueTrackerUrl);
                    }
                };
            }

            return td;
        }

        public static void RetryUntilCanceled(
            this ITaskDialogProvider dialogProvider,
            Action<int> action,
            string title,
            string failedText,
            string expandControlText,
            string retryButtonText,
            string cancelButtonText,
            Func<Exception, bool> canRetry = null
        ) {
            for (int retryCount = 1; ; ++retryCount) {
                try {
                    action(retryCount);
                    return;
                } catch (Exception ex) when (!ex.IsCriticalException() && canRetry?.Invoke(ex) != false) {
                    var td = dialogProvider.CreateTaskDialog();
                    td.Title = title;
                    td.MainInstruction = failedText;
                    td.Content = ex.Message;
                    td.CollapsedControlText = expandControlText;
                    td.ExpandedControlText = expandControlText;
                    td.ExpandedInformation = ex.ToString();

                    var retry = new TaskDialogButton(retryButtonText);
                    var cancel = new TaskDialogButton(cancelButtonText);
                    td.Buttons.Add(retry);
                    td.Buttons.Add(cancel);

                    var button = td.ShowModal();

                    if (button == cancel) {
                        throw new OperationCanceledException();
                    }
                }
            }
        }

        public static T RetryUntilCanceled<T>(
            this ITaskDialogProvider dialogProvider,
            Func<int, T> func,
            string title,
            string failedText,
            string expandControlText,
            string retryButtonText,
            string cancelButtonText,
            Func<Exception, bool> canRetry = null
        ) {
            for (int retryCount = 1; ; ++retryCount) {
                try {
                    return func(retryCount);
                } catch (Exception ex) when (!ex.IsCriticalException() && canRetry?.Invoke(ex) != false) {
                    var td = dialogProvider.CreateTaskDialog();
                    td.Title = title;
                    td.MainInstruction = failedText;
                    td.Content = ex.Message;
                    td.CollapsedControlText = expandControlText;
                    td.ExpandedControlText = expandControlText;
                    td.ExpandedInformation = ex.ToString();

                    var retry = new TaskDialogButton(retryButtonText);
                    var cancel = new TaskDialogButton(cancelButtonText);
                    td.Buttons.Add(retry);
                    td.Buttons.Add(cancel);

                    var button = td.ShowModal();

                    if (button == cancel) {
                        throw new OperationCanceledException();
                    }
                }
            }
        }
    }
}