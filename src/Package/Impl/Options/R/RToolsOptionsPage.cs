﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.ComponentModel;
using System.Drawing.Design;
using Microsoft.Common.Core.Enums;
using Microsoft.Common.Core.Shell;
using Microsoft.R.Components.Settings;
using Microsoft.R.Interpreters;
using Microsoft.R.Host.Client.Install;
using Microsoft.R.Support.Settings;
using Microsoft.R.Support.Settings.Definitions;
using Microsoft.VisualStudio.R.Package.Options.Attributes;
using Microsoft.VisualStudio.R.Package.Options.R.Tools;
using Microsoft.VisualStudio.R.Package.Shell;
using Microsoft.VisualStudio.R.Package.Telemetry;
using Microsoft.VisualStudio.Shell;

namespace Microsoft.VisualStudio.R.Package.Options.R {
    public class RToolsOptionsPage : DialogPage {
        private bool _allowLoadingFromStorage;
        private bool _applied;

        public RToolsOptionsPage() {
            this.SettingsRegistryPath = @"UserSettings\R_Tools";
        }

        [LocCategory("Settings_GeneralCategory")]
        [CustomLocDisplayName("Settings_CranMirror")]
        [LocDescription("Settings_CranMirror_Description")]
        [TypeConverter(typeof(CranMirrorTypeConverter))]
        [DefaultValue(null)]
        public string CranMirror {
            get { return RToolsSettings.Current.CranMirror; }
            set { RToolsSettings.Current.CranMirror = value; }
        }

        [LocCategory("Settings_WorkspaceCategory")]
        [CustomLocDisplayName("Settings_LoadRDataOnProjectLoad")]
        [LocDescription("Settings_LoadRDataOnProjectLoad_Description")]
        [TypeConverter(typeof(YesNoAskTypeConverter))]
        [DefaultValue(YesNoAsk.No)]
        public YesNoAsk LoadRDataOnProjectLoad {
            get { return RToolsSettings.Current.LoadRDataOnProjectLoad; }
            set { RToolsSettings.Current.LoadRDataOnProjectLoad = value; }
        }

        [LocCategory("Settings_WorkspaceCategory")]
        [CustomLocDisplayName("Settings_SaveRDataOnProjectUnload")]
        [LocDescription("Settings_SaveRDataOnProjectUnload_Description")]
        [TypeConverter(typeof(YesNoAskTypeConverter))]
        [DefaultValue(YesNoAsk.No)]
        public YesNoAsk SaveRDataOnProjectUnload {
            get { return RToolsSettings.Current.SaveRDataOnProjectUnload; }
            set { RToolsSettings.Current.SaveRDataOnProjectUnload = value; }
        }

        [LocCategory("Settings_HistoryCategory")]
        [CustomLocDisplayName("Settings_AlwaysSaveHistory")]
        [LocDescription("Settings_AlwaysSaveHistory_Description")]
        [DefaultValue(true)]
        public bool AlwaysSaveHistory {
            get { return RToolsSettings.Current.AlwaysSaveHistory; }
            set { RToolsSettings.Current.AlwaysSaveHistory = value; }
        }

        [LocCategory("Settings_HistoryCategory")]
        [CustomLocDisplayName("Settings_ClearFilterOnAddHistory")]
        [LocDescription("Settings_ClearFilterOnAddHistory_Description")]
        [DefaultValue(true)]
        public bool ClearFilterOnAddHistory {
            get { return RToolsSettings.Current.ClearFilterOnAddHistory; }
            set { RToolsSettings.Current.ClearFilterOnAddHistory = value; }
        }

        [LocCategory("Settings_HistoryCategory")]
        [CustomLocDisplayName("Settings_MultilineHistorySelection")]
        [LocDescription("Settings_MultilineHistorySelection_Description")]
        [DefaultValue(false)]
        public bool MultilineHistorySelection {
            get { return RToolsSettings.Current.MultilineHistorySelection; }
            set { RToolsSettings.Current.MultilineHistorySelection = value; }
        }

        [LocCategory("Settings_REngineCategory")]
        [CustomLocDisplayName("Settings_RCommandLineArguments")]
        [LocDescription("Settings_RCommandLineArguments_Description")]
        public string RCommandLineArguments {
            get { return RToolsSettings.Current.RCommandLineArguments; }
            set { RToolsSettings.Current.RCommandLineArguments = value; }
        }

        [LocCategory("Settings_REngineCategory")]
        [CustomLocDisplayName("Settings_RBasePath")]
        [LocDescription("Settings_RBasePath_Description")]
        [Editor(typeof(ChooseRFolderUIEditor), typeof(UITypeEditor))]
        public string RVersion {
            get { return RToolsSettings.Current.RBasePath; }
            set {
                value = ValidateRBasePath(value);
                if (value != null) {
                    if (RToolsSettings.Current.RBasePath != value && !_allowLoadingFromStorage) {
                        VsAppShell.Current.ShowMessage(Resources.RPathChanged_RestartRToApplyChanges, MessageButtons.OK);
                    }
                    RToolsSettings.Current.RBasePath = value;
                }
            }
        }

        [LocCategory("Settings_REngineCategory")]
        public Uri BrokerUri {
            get { return RToolsSettings.Current.BrokerUri; }
            set { RToolsSettings.Current.BrokerUri = value == new Uri("") ? null : value; }
        }

        [LocCategory("Settings_REngineCategory")]
        [CustomLocDisplayName("Settings_RCodePage")]
        [LocDescription("Settings_RCodePage_Description")]
        [TypeConverter(typeof(EncodingTypeConverter))]
        [DefaultValue(0)]
        public int RCodePage {
            get { return RToolsSettings.Current.RCodePage; }
            set { RToolsSettings.Current.RCodePage = value; }
        }

        [LocCategory("Settings_DebuggingCategory")]
        [CustomLocDisplayName("Settings_EvaluateActiveBindings")]
        [LocDescription("Settings_EvaluateActiveBindings_Description")]
        [DefaultValue(true)]
        public bool EvaluateActiveBindings {
            get { return RToolsSettings.Current.EvaluateActiveBindings; }
            set { RToolsSettings.Current.EvaluateActiveBindings = value; }
        }

        [LocCategory("Settings_DebuggingCategory")]
        [CustomLocDisplayName("Settings_ShowDotPrefixedVariables")]
        [LocDescription("Settings_ShowDotPrefixedVariables_Description")]
        [DefaultValue(false)]
        public bool ShowDotPrefixedVariables {
            get { return RToolsSettings.Current.ShowDotPrefixedVariables; }
            set { RToolsSettings.Current.ShowDotPrefixedVariables = value; }
        }

        [LocCategory("Settings_HelpCategory")]
        [CustomLocDisplayName("Settings_HelpBrowser")]
        [LocDescription("Settings_HelpBrowser_Description")]
        [TypeConverter(typeof(HelpBrowserTypeConverter))]
        [DefaultValue(HelpBrowserType.Automatic)]
        public HelpBrowserType HelpBrowser {
            get { return RToolsSettings.Current.HelpBrowserType; }
            set { RToolsSettings.Current.HelpBrowserType = value; }
        }

        [LocCategory("Settings_HelpCategory")]
        [CustomLocDisplayName("Settings_WebHelpSearchString")]
        [LocDescription("Settings_WebHelpSearchString_Description")]
        [DefaultValue("R site:stackoverflow.com")]
        public string WebHelpSearchString {
            get { return RToolsSettings.Current.WebHelpSearchString; }
            set { RToolsSettings.Current.WebHelpSearchString = value; }
        }

        [LocCategory("Settings_HelpCategory")]
        [CustomLocDisplayName("Settings_WebHelpSearchBrowserType")]
        [LocDescription("Settings_WebHelpSearchBrowserType_Description")]
        [TypeConverter(typeof(BrowserTypeConverter))]
        [DefaultValue(BrowserType.Internal)]
        public BrowserType WebHelpSearchBrowserType {
            get { return RToolsSettings.Current.WebHelpSearchBrowserType; }
            set { RToolsSettings.Current.WebHelpSearchBrowserType = value; }
        }

        [LocCategory("Settings_ShinyCategory")]
        [CustomLocDisplayName("Settings_ShinyBrowserType")]
        [LocDescription("Settings_ShinyBrowserType_Description")]
        [TypeConverter(typeof(BrowserTypeConverter))]
        [DefaultValue(BrowserType.Internal)]
        public BrowserType ShinyBrowserType {
            get { return RToolsSettings.Current.ShinyBrowserType; }
            set { RToolsSettings.Current.ShinyBrowserType = value; }
        }

        [LocCategory("Settings_MarkdownCategory")]
        [CustomLocDisplayName("Settings_MarkdownBrowserType")]
        [LocDescription("Settings_MarkdownBrowserType_Description")]
        [TypeConverter(typeof(BrowserTypeConverter))]
        [DefaultValue(BrowserType.External)]
        public BrowserType MarkdownBrowserType {
            get { return RToolsSettings.Current.MarkdownBrowserType; }
            set { RToolsSettings.Current.MarkdownBrowserType = value; }
        }

        [LocCategory("Settings_GeneralCategory")]
        [CustomLocDisplayName("Settings_SurveyNewsCheck")]
        [LocDescription("Settings_SurveyNewsCheck_Description")]
        [TypeConverter(typeof(SurveyNewsPolicyTypeConverter))]
        [DefaultValue(SurveyNewsPolicy.CheckOnceWeek)]
        public SurveyNewsPolicy SurveyNewsCheck {
            get { return RToolsSettings.Current.SurveyNewsCheck; }
            set { RToolsSettings.Current.SurveyNewsCheck = value; }
        }

        /// <summary>
        /// The last time that we contacted the survey/news server.
        /// Used in conjunction with <see cref="SurveyNewsLastCheck"/>
        /// option to determine if we should contact the survey/news server
        /// when an R project is opened.
        /// </summary>
        [Browsable(false)]
        public DateTime SurveyNewsLastCheck {
            get { return RToolsSettings.Current.SurveyNewsLastCheck; }
            set { RToolsSettings.Current.SurveyNewsLastCheck = value; }
        }

        [Browsable(false)]
        public string SurveyNewsFeedUrl {
            get { return RToolsSettings.Current.SurveyNewsFeedUrl; }
            set { RToolsSettings.Current.SurveyNewsFeedUrl = value; }
        }

        [Browsable(false)]
        public string SurveyNewsIndexUrl {
            get { return RToolsSettings.Current.SurveyNewsIndexUrl; }
            set { RToolsSettings.Current.SurveyNewsIndexUrl = value; }
        }

        [Browsable(false)]
        [DefaultValue(true)]
        public bool ShowPackageManagerDisclaimer {
            get { return RToolsSettings.Current.ShowPackageManagerDisclaimer; }
            set { RToolsSettings.Current.ShowPackageManagerDisclaimer = value; }
        }

        /// <summary>
        /// REPL working directory: not exposed in Tools | Options dialog,
        /// only saved along with other settings.
        /// </summary>
        internal string WorkingDirectory {
            get { return RToolsSettings.Current.WorkingDirectory; }
            set { RToolsSettings.Current.WorkingDirectory = value; }
        }

        internal string[] WorkingDirectoryList {
            get { return RToolsSettings.Current.WorkingDirectoryList; }
            set { RToolsSettings.Current.WorkingDirectoryList = value; }
        }

        /// <summary>
        /// Loads all values from persistent storage. Typically called when package loads.
        /// </summary>
        public void LoadSettings() {
            _allowLoadingFromStorage = true;
            try {
                LoadSettingsFromStorage();
            } finally {
                _allowLoadingFromStorage = false;
            }
        }

        /// <summary>
        /// Saves all values to persistent storage. Typically called when package unloads.
        /// </summary>
        public void SaveSettings() {
            SaveSettingsToStorage();
        }

        public override void LoadSettingsFromStorage() {
            // Only permit loading when loading was initiated via LoadSettings().
            // Otherwise dialog will load values from registry instead of using
            // ones currently set in memory.
            if (_allowLoadingFromStorage) {
                base.LoadSettingsFromStorage();
            }
        }

        private string ValidateRBasePath(string path) {
            // If path is null, folder selector dialog was canceled
            if (path != null) {
                var ri = new RInstallation();
                path = ri.NormalizeRPath(path);
                bool valid = ri.VerifyRIsInstalled(VsAppShell.Current, null, path, showErrors: !_allowLoadingFromStorage);
                if (!valid) {
                    path = null; // Prevents assignment of bad values to the property.
                }
            }

            return path;
        }

        protected override void OnClosed(EventArgs e) {
            if (!_applied) {
                // On cancel load previously saved settings back
                LoadSettings();
            }
            _applied = false;
            base.OnClosed(e);
        }
        protected override void OnActivate(CancelEventArgs e) {
            // Save in-memory settings to storage. In case dialog
            // is canceled with some settings modified we will be
            // able to restore them from storage.
            SaveSettingsToStorage();
            base.OnActivate(e);
        }

        protected override void OnApply(PageApplyEventArgs e) {
            if (e.ApplyBehavior == ApplyKind.Apply) {
                _applied = true;
                // On OK persist settings
                SaveSettingsToStorage();
                RtvsTelemetry.Current.ReportSettings();
            }
            base.OnApply(e);
        }
    }
}
