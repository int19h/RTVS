﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Common.Core;
using Microsoft.Common.Core.Disposables;
using Microsoft.Common.Core.Enums;
using Microsoft.Languages.Editor.Shell;
using Microsoft.R.Components.InteractiveWorkflow;
using Microsoft.R.Components.Settings;
using Microsoft.R.Components.Settings.Mirrors;
using Microsoft.R.Interpreters;
using Microsoft.R.Host.Client;
using Microsoft.R.Host.Client.Install;
using Microsoft.R.Host.Client.Session;
using Microsoft.R.Support.Settings;
using Microsoft.R.Support.Settings.Definitions;
using Microsoft.VisualStudio.R.Package.Shell;
using Microsoft.VisualStudio.R.Package.SurveyNews;
using Microsoft.VisualStudio.R.Packages.R;
using Microsoft.VisualStudio.Shell.Interop;

namespace Microsoft.VisualStudio.R.Package.Options.R {
    [Export(typeof(IRSettings))]
    [Export(typeof(IRToolsSettings))]
    internal sealed class RToolsSettingsImplementation : IRToolsSettings {
        private const int MaxDirectoryEntries = 8;
        private string _cranMirror;
        private string _workingDirectory;
        private int _codePage;
        private bool _showPackageManagerDisclaimer = true;
        private string _rBasePath;

        /// <summary>
        /// Path to 64-bit R installation such as 
        /// 'C:\Program Files\R\R-3.2.2' without bin\x64
        /// </summary>
        public string RBasePath {
            get { return _rBasePath; }
            set {
                if (_rBasePath.EqualsIgnoreCase(value)) {
                    return;
                }
                _rBasePath = value;
                var workflow = VsAppShell.Current.ExportProvider.GetExportedValue<IRInteractiveWorkflowProvider>().GetOrCreate();
                workflow.Connections.AddOrUpdateLocalConnection(_rBasePath, _rBasePath);
            }
        }

        public Uri BrokerUri { get; set; }

        public YesNoAsk LoadRDataOnProjectLoad { get; set; } = YesNoAsk.No;

        public YesNoAsk SaveRDataOnProjectUnload { get; set; } = YesNoAsk.No;

        public bool AlwaysSaveHistory { get; set; } = true;

        public bool ClearFilterOnAddHistory { get; set; } = true;

        public bool MultilineHistorySelection { get; set; } = true;

        public bool ShowPackageManagerDisclaimer {
            get { return _showPackageManagerDisclaimer; }
            set {
                using (SaveSettings()) {
                    _showPackageManagerDisclaimer = value;
                }
            }
        }

        public string CranMirror {
            get { return _cranMirror; }
            set {
                _cranMirror = value;
                SetMirrorToSession().DoNotWait();
            }
        }

        public int RCodePage {
            get { return _codePage; }
            set {
                _codePage = value;
                SetSessionCodePage().DoNotWait();
            }
        }

        public string WorkingDirectory {
            get { return _workingDirectory; }
            set {
                var newDirectory = value;
                var newDirectoryIsRoot = newDirectory.Length >= 2 && newDirectory[newDirectory.Length - 2] == Path.VolumeSeparatorChar;
                if (newDirectory.EndsWithOrdinal("\\") && !newDirectoryIsRoot) {
                    newDirectory = newDirectory.Substring(0, newDirectory.Length - 1);
                }

                _workingDirectory = newDirectory;
                UpdateWorkingDirectoryList(newDirectory);

                if (EditorShell.HasShell) {
                    VsAppShell.Current.DispatchOnUIThread(() => {
                        IVsUIShell shell = VsAppShell.Current.GetGlobalService<IVsUIShell>(typeof(SVsUIShell));
                        shell.UpdateCommandUI(1);
                    });
                }
            }
        }

        public string[] WorkingDirectoryList { get; set; } = new string[0];
        public string RCommandLineArguments { get; set; }
        public HelpBrowserType HelpBrowserType { get; set; }
        public bool ShowDotPrefixedVariables { get; set; }
        public SurveyNewsPolicy SurveyNewsCheck { get; set; } = SurveyNewsPolicy.CheckOnceWeek;
        public DateTime SurveyNewsLastCheck { get; set; }
        public string SurveyNewsFeedUrl { get; set; } = SurveyNewsUrls.Feed;
        public string SurveyNewsIndexUrl { get; set; } = SurveyNewsUrls.Index;
        public bool EvaluateActiveBindings { get; set; } = true;
        public string WebHelpSearchString { get; set; } = "R site:stackoverflow.com";
        public BrowserType WebHelpSearchBrowserType { get; set; } = BrowserType.Internal;
        public BrowserType ShinyBrowserType { get; set; } = BrowserType.Internal;
        public BrowserType MarkdownBrowserType { get; set; } = BrowserType.External;

        public RToolsSettingsImplementation() {
            // Default settings. Will be overwritten with actual
            // settings (if any) when settings are loaded from storage
            _rBasePath = new RInstallation().GetRInstallPath();
            _workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        private async Task SetMirrorToSession() {
            IRSessionProvider sessionProvider = VsAppShell.Current.ExportProvider.GetExportedValue<IRSessionProvider>();
            var sessions = sessionProvider.GetSessions();
            string mirrorName = RToolsSettings.Current.CranMirror;
            string mirrorUrl = CranMirrorList.UrlFromName(mirrorName);

            foreach (var s in sessions.Where(s => s.IsHostRunning)) {
                try {
                    using (var eval = await s.BeginEvaluationAsync()) {
                        await eval.SetVsCranSelectionAsync(mirrorUrl);
                    }
                } catch (RException) {
                } catch (OperationCanceledException) {
                }
            }
        }

        private async Task SetSessionCodePage() {
            IRSessionProvider sessionProvider = VsAppShell.Current.ExportProvider.GetExportedValue<IRSessionProvider>();
            var sessions = sessionProvider.GetSessions();
            var cp = RToolsSettings.Current.RCodePage;
 
            foreach (var s in sessions.Where(s => s.IsHostRunning)) {
                try {
                    using (var eval = await s.BeginEvaluationAsync()) {
                        await eval.SetCodePageAsync(cp);
                    }
                } catch (OperationCanceledException) { }
            }
        }

        private void UpdateWorkingDirectoryList(string newDirectory) {
            List<string> list = new List<string>(WorkingDirectoryList);
            if (!list.Contains(newDirectory, StringComparer.OrdinalIgnoreCase)) {
                list.Insert(0, newDirectory);
                if (list.Count > MaxDirectoryEntries) {
                    list.RemoveAt(list.Count - 1);
                }

                WorkingDirectoryList = list.ToArray();
            }
        }

        private IDisposable SaveSettings() {
            var page = RPackage.Current.GetDialogPage(typeof(RToolsOptionsPage)) as RToolsOptionsPage;
            return Disposable.Create(() => page?.SaveSettings());
        }
    }
}
