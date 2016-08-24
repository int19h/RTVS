﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.R.Components.Controller;
using Microsoft.R.Components.InteractiveWorkflow;

namespace Microsoft.R.Components.Plots.Implementation.Commands {
    internal sealed class PlotDeviceNewCommand : InteractiveWorkflowAsyncCommand, IAsyncCommand {
        public PlotDeviceNewCommand(IRInteractiveWorkflow interactiveWorkflow) :
            base(interactiveWorkflow) {
        }

        public CommandStatus Status {
            get {
                return CommandStatus.SupportedAndEnabled;
            }
        }

        public async Task<CommandResult> InvokeAsync() {
            try {
                await InteractiveWorkflow.Plots.NewDeviceAsync(-1);
            } catch (RPlotManagerException ex) {
                InteractiveWorkflow.Shell.ShowErrorMessage(ex.Message);
            } catch (OperationCanceledException) {
            }

            return CommandResult.Executed;
        }
    }
}
