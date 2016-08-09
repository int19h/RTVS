﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using Microsoft.R.Components.Settings;

namespace Microsoft.R.Components.Test.Stubs {
    public sealed class RSettingsStub : IRSettings {
        public bool AlwaysSaveHistory { get; set; }
        public bool ClearFilterOnAddHistory { get; set; }
        public bool MultilineHistorySelection { get; set; }
        public string RBasePath { get; set; }
        public string CranMirror { get; set; }
        public string RCommandLineArguments { get; set; }
        public string WorkingDirectory { get; set; }
        public bool ShowPackageManagerDisclaimer { get; set; }
        public HelpBrowserType HelpBrowserType { get; set; }
        public int RCodePage { get; set; }
        public bool EvaluateActiveBindings { get; set; }

        public Uri BrokerUri { get; set; }
    }
}
