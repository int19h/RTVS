﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.ComponentModel.Composition;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Microsoft.Common.Core.Shell;
using Microsoft.R.Components.ConnectionManager;
using Microsoft.R.Components.History;
using Microsoft.R.Components.InteractiveWorkflow;
using Microsoft.R.Components.InteractiveWorkflow.Implementation;
using Microsoft.R.Components.PackageManager;
using Microsoft.R.Components.Plots;
using Microsoft.R.Components.Settings;
using Microsoft.R.Host.Client;
using Microsoft.R.Host.Client.Host;
using Microsoft.UnitTests.Core.Mef;

namespace Microsoft.R.Components.Test.Fakes.InteractiveWindow {
    [ExcludeFromCodeCoverage]
    [Export(typeof(IRInteractiveWorkflowProvider))]
    [Export(typeof(TestRInteractiveWorkflowProvider))]
    [PartMetadata(PartMetadataAttributeNames.SkipInEditorTestCompositionCatalog, null)]
    public class TestRInteractiveWorkflowProvider : IRInteractiveWorkflowProvider, IDisposable {
        private readonly IRSessionProvider _sessionProvider;
        private readonly IConnectionManagerProvider _connectionManagerProvider;
        private readonly IRHistoryProvider _historyProvider;
        private readonly IRPackageManagerProvider _packagesProvider;
        private readonly IRPlotManagerProvider _plotsProvider;
        private readonly ICoreShell _shell;
        private readonly IRSettings _settings;
        private readonly IActiveWpfTextViewTracker _activeTextViewTracker;
        private readonly IDebuggerModeTracker _debuggerModeTracker;
        private readonly IRHostBrokerConnector _brokerConnector;

        private Lazy<IRInteractiveWorkflow> _instanceLazy;
        public IRSessionCallback HostClientApp { get; set; }

        public string TestName { get; set; }

        [ImportingConstructor]
        public TestRInteractiveWorkflowProvider(IRSessionProvider sessionProvider
            , IConnectionManagerProvider connectionManagerProvider
            , IRHistoryProvider historyProvider
            , IRPackageManagerProvider packagesProvider
            , IRPlotManagerProvider plotsProvider
            , IActiveWpfTextViewTracker activeTextViewTracker
            , IDebuggerModeTracker debuggerModeTracker
            // Required for the tests that create TestRInteractiveWorkflowProvider explicitly
            , [Import(AllowDefault = true)] IRHostBrokerConnector brokerConnector
            , ICoreShell shell
            , IRSettings settings) {
            _sessionProvider = sessionProvider;
            _connectionManagerProvider = connectionManagerProvider;
            _historyProvider = historyProvider;
            _packagesProvider = packagesProvider;
            _plotsProvider = plotsProvider;
            _activeTextViewTracker = activeTextViewTracker;
            _debuggerModeTracker = debuggerModeTracker;
            _brokerConnector = brokerConnector;
            _shell = shell;
            _settings = settings;
        }

        public void Dispose() {
            if (_instanceLazy?.IsValueCreated == true) {
                _instanceLazy?.Value?.Dispose();
            }
        }

        public IRInteractiveWorkflow GetOrCreate() {
            Interlocked.CompareExchange(ref _instanceLazy, new Lazy<IRInteractiveWorkflow>(CreateRInteractiveWorkflow), null);
            return _instanceLazy.Value;
        }
        
        private IRInteractiveWorkflow CreateRInteractiveWorkflow() {
            return new RInteractiveWorkflow(TestName
                , _sessionProvider
                , _connectionManagerProvider
                , _historyProvider
                , _packagesProvider
                , _plotsProvider
                , _activeTextViewTracker
                , _debuggerModeTracker
                , _brokerConnector ?? new RHostBrokerConnector(name: TestName)
                , _shell
                , _settings
                , DisposeInstance);
        }

        private void DisposeInstance() {
            _instanceLazy = null;
        }
    }
}
