﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Common.Core;
using Microsoft.Common.Core.IO;
using Microsoft.Common.Core.Shell;
using Microsoft.R.Components.Help;
using Microsoft.R.Components.PackageManager;
using Microsoft.R.Components.Settings;
using Microsoft.R.Components.Settings.Mirrors;
using Microsoft.R.Host.Client;
using Microsoft.R.Host.Client.Host;
using Microsoft.VisualStudio.InteractiveWindow;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.R.Components.InteractiveWorkflow.Implementation {
    internal sealed class RSessionCallback : IRSessionCallback {
        private readonly IRInteractiveWorkflow _workflow;
        private readonly IInteractiveWindow _interactiveWindow;
        private readonly IRSession _session;
        private readonly IRSettings _settings;
        private readonly ICoreShell _coreShell;
        private readonly IFileSystem _fileSystem;

        public RSessionCallback(IInteractiveWindow interactiveWindow, IRSession session, IRSettings settings, ICoreShell coreShell, IFileSystem fileSystem) {
            _interactiveWindow = interactiveWindow;
            _session = session;
            _settings = settings;
            _coreShell = coreShell;
            _fileSystem = fileSystem;

            var workflowProvider = _coreShell.ExportProvider.GetExportedValue<IRInteractiveWorkflowProvider>();
            _workflow = workflowProvider.GetOrCreate();
        }

        /// <summary>
        /// Displays error message in the host-specific UI
        /// </summary>
        public Task ShowErrorMessage(string message, CancellationToken cancellationToken = default(CancellationToken)) => _coreShell.ShowErrorMessageAsync(message, cancellationToken);

        /// <summary>
        /// Displays message with specified buttons in a host-specific UI
        /// </summary>
        public Task<MessageButtons> ShowMessageAsync(string message, MessageButtons buttons, CancellationToken cancellationToken) => _coreShell.ShowMessageAsync(message, buttons, cancellationToken);

        /// <summary>
        /// Displays R help URL in a browser on in the host app-provided window
        /// </summary>
        public async Task ShowHelpAsync(string url, CancellationToken cancellationToken) {
            await _coreShell.SwitchToMainThreadAsync(cancellationToken);
            if (_settings.HelpBrowserType == HelpBrowserType.External) {
                Process.Start(url);
            } else {
                var container = _coreShell.ExportProvider.GetExportedValue<IHelpVisualComponentContainerFactory>().GetOrCreate();
                container.Component.Navigate(url);
            }
        }

        /// <summary>
        /// Displays R plot in the host app-provided window
        /// </summary>
        public Task Plot(PlotMessage plot, CancellationToken ct)
            => _workflow.Plots.LoadPlotAsync(plot, ct);

        public Task<LocatorResult> Locator(Guid deviceId, CancellationToken ct)
            => _workflow.Plots.StartLocatorModeAsync(deviceId, ct);

        public Task<PlotDeviceProperties> PlotDeviceCreate(Guid deviceId, CancellationToken ct)
            => _workflow.Plots.DeviceCreatedAsync(deviceId, ct);

        public async Task PlotDeviceDestroy(Guid deviceId, CancellationToken ct) {
            await _workflow.Plots.DeviceDestroyedAsync(deviceId, ct);
        }

        public Task<string> ReadUserInput(string prompt, int maximumLength, CancellationToken ct) {
            _coreShell.DispatchOnUIThread(() => _interactiveWindow.Write(prompt));
            return Task.Run(() => {
                using (var reader = _interactiveWindow.ReadStandardInput()) {
                    return reader != null ? Task.FromResult(reader.ReadToEnd()) : Task.FromResult("\n");
                }
            }, ct);
        }

        /// <summary>
        /// Given CRAN mirror name returns URL
        /// </summary>
        public string CranUrlFromName(string mirrorName) {
            return CranMirrorList.UrlFromName(mirrorName);
        }

        public Task ViewObjectAsync(string expression, string title, CancellationToken cancellationToken = default(CancellationToken)) {
            var viewer = _coreShell.ExportProvider.GetExportedValue<IObjectViewer>();
            return viewer?.ViewObjectDetails(_session, REnvironments.GlobalEnv, expression, title, cancellationToken) ?? Task.CompletedTask;
        }

        public async Task ViewLibraryAsync(CancellationToken cancellationToken = default(CancellationToken)) {
            await _coreShell.SwitchToMainThreadAsync(cancellationToken);
            var containerFactory = _coreShell.ExportProvider.GetExportedValue<IRPackageManagerVisualComponentContainerFactory>();
            _workflow.Packages.GetOrCreateVisualComponent(containerFactory).Container.Show(focus: true, immediate: false);
        }

        public async Task ViewFile(string fileName, string tabName, bool deleteFile, CancellationToken cancellationToken = default(CancellationToken)) {
            var viewer = _coreShell.ExportProvider.GetExportedValue<IObjectViewer>();
            var task = Task.CompletedTask;

            if (_session.IsRemote) {
                using (var dts = new DataTransferSession(_session, _fileSystem)) {
                    // TODO: handle progress for large files
                    try {
                        await dts.FetchFileToLocalTempAsync(fileName.ToRPath(), null, cancellationToken);
                        fileName = _fileSystem.GetDownloadsPath(Path.GetFileName(fileName));
                        await viewer?.ViewFile(fileName, tabName, deleteFile, cancellationToken);
                    } catch (REvaluationException) { } catch (RHostDisconnectedException) { }
                }
            } else {
                await viewer?.ViewFile(fileName, tabName, deleteFile, cancellationToken);
            }
        }

        public async Task<string> FetchFileAsync(string remoteFileName, ulong remoteBlobId, string localPath, CancellationToken cancellationToken) {
            await _coreShell.SwitchToMainThreadAsync(cancellationToken);

            if (!string.IsNullOrEmpty(localPath)) {
                if (_fileSystem.DirectoryExists(localPath)) {
                    localPath = Path.Combine(localPath, remoteFileName);
                }
            } else {
                localPath = _fileSystem.GetDownloadsPath(remoteFileName);
            }

            try {
                var message = Resources.Progress_FetchingFile.FormatInvariant(remoteFileName);
                _coreShell.ProgressDialog.Show(async (progress, ct) => {
                    using (DataTransferSession dts = new DataTransferSession(_session, _fileSystem)) {
                        await dts.FetchAndDecompressFileAsync(remoteBlobId, localPath, progress, message, cancellationToken);
                    }
                }, message);
            } catch (Exception ex) {
                _coreShell.ShowErrorMessage(Resources.Error_UnableToTransferFile.FormatInvariant(localPath, ex.Message));
                return string.Empty;
            }
            return localPath;
        }

        public async Task<string> UploadFileAsync(string fileName, CancellationToken cancellationToken) {
            await _coreShell.SwitchToMainThreadAsync(cancellationToken);

            try {
                var message = Resources.Progress_UploadingFile.FormatInvariant(fileName);
                string remoteFileName = null;
                _coreShell.ProgressDialog.Show(async (progress, ct) => {
                    using (DataTransferSession dts = new DataTransferSession(_session, _fileSystem)) {
                        remoteFileName = await dts.CopyFileToRemoteTempAsync(fileName, true, progress, message, cancellationToken);
                    }
                }, message);
                return remoteFileName;
            } catch (Exception ex) {
                _coreShell.ShowErrorMessage(Resources.Error_UnableToTransferFile.FormatInvariant(fileName, ex.Message));
                return null;
            }
        }
    }
}
