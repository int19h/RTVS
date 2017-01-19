// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Common.Core;
using Microsoft.Common.Core.Shell;
using Microsoft.R.Host.Client;

namespace Microsoft.R.Components.InteractiveWorkflow.Implementation {
    public sealed class InteractiveWindowConsole : IConsole {
        public static readonly TimeSpan AutoFlushDelay = TimeSpan.FromMilliseconds(500);

        private readonly ICoreShell _coreShell;
        private readonly IRInteractiveWorkflow _workflow;
        private IInteractiveWindowVisualComponent _component;
        private CarriageReturnProcessor _crProcessor;

        private struct Output {
            public readonly string Text;
            public readonly bool IsError;

            public Output(string text, bool isError) {
                Text = text;
                IsError = isError;
            }
        }

        private readonly ConcurrentQueue<Output> _outputQueue = new ConcurrentQueue<Output>();

        public InteractiveWindowConsole(ICoreShell coreShell, IRInteractiveWorkflow workflow) {
            _coreShell = coreShell;
            _workflow = workflow;

            AutoFlushWorker();
        }

        private async Task EnsureComponent() {
            await _workflow.Shell.SwitchToMainThreadAsync();
            if (_component == null) {
                _component = await _workflow.GetOrCreateVisualComponentAsync();
                _component.Container.Show(focus: false, immediate: false);
                _crProcessor = new CarriageReturnProcessor(_coreShell, _component.InteractiveWindow);
            }
        }

        public void Write(string text) => QueueWrite(text, false);

        public void WriteLine(string text) => Write(text + Environment.NewLine);

        public void WriteError(string text) => QueueWrite(text, true);

        public void WriteErrorLine(string text) => WriteError(text + Environment.NewLine);

        private void QueueWrite(string text, bool isError) {
            _outputQueue.Enqueue(new Output(text, isError));
        }

        public async Task FlushAsync() {
            await _workflow.Shell.SwitchToMainThreadAsync();
            await EnsureComponent();

            // Combine output entries of the same type in a single text block before printing to maximize REPL throughput
            // on large volume of text. When output type changes, the output combined so far needs to be written out.
            //
            // Also, if the message begins with '\r', it's a progress bar or similar arrangement to overwrite the current
            // line. This is handled by CarriageReturnProcessor, but it is specifically triggered by leading '\r', so avoid
            // combining such entries in a way that would result in '\r' ending up in the middle. 
            var sb = new StringBuilder();
            Output output;
            bool isError = false;
            while (true) {
                bool hasOutput = _outputQueue.TryDequeue(out output);

                if (!hasOutput || output.IsError != isError || output.Text.StartsWith("\r", StringComparison.Ordinal)) {
                    if (sb.Length != 0) {
                        string text = sb.ToString();

                        if (isError) {
                            _component.InteractiveWindow.WriteError(text);
                        } else {
                            if (!_crProcessor.ProcessMessage(text)) {
                                _component.InteractiveWindow.Write(text);
                            }
                        }

                        sb.Clear();
                    }
                    isError = output.IsError;
                }

                if (!hasOutput) {
                    break;
                }

                sb.Append(output.Text);
            }
        }

        private async void AutoFlushWorker() {
            while (true) {
                await Task.Delay(AutoFlushDelay);
                await FlushAsync();
            }
        }

        public async Task<bool> PromptYesNoAsync(string text, CancellationToken cancellationToken) {
            var result = await _workflow.Shell.ShowMessageAsync(text, MessageButtons.YesNo, cancellationToken);
            return result == MessageButtons.Yes;
        }
    }
}