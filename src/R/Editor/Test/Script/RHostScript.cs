﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using Microsoft.R.Host.Client.Host;
using Microsoft.R.Interpreters;
using Microsoft.R.Support.Settings;

namespace Microsoft.R.Host.Client.Test.Script {
    [ExcludeFromCodeCoverage]
    public class RHostScript : IDisposable {
        private bool _disposed = false;
        private IRHostBrokerConnector _connector = new RHostBrokerConnector();

        public IRSessionProvider SessionProvider { get; private set; }
        public IRSession Session { get; private set; }

        public static Version RVersion => new RInstallation().GetInstallationData(RToolsSettings.Current.RBasePath, new SupportedRVersionRange()).Version;

        public RHostScript(IRSessionProvider sessionProvider, IRSessionCallback clientApp = null) {
            SessionProvider = sessionProvider;

            Session = SessionProvider.GetOrCreate(GuidList.InteractiveWindowRSessionGuid, _connector);
            Session.IsHostRunning.Should().BeFalse();
            
            Session.StartHostAsync(new RHostStartupInfo {
                Name = "RHostScript",
                RHostCommandLineArguments = RToolsSettings.Current.RCommandLineArguments,
                CranMirrorName = RToolsSettings.Current.CranMirror,
                CodePage = RToolsSettings.Current.RCodePage
            }, clientApp ?? new RHostClientTestApp(), 50000).Wait();
        }

        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing) {
            if (_disposed) {
                return;
            }

            if (disposing) {
                if (Session != null) {
                    Session.StopHostAsync().Wait(15000);
                    Debug.Assert(!Session.IsHostRunning);
                }

                if (SessionProvider != null) {
                    SessionProvider = null;
                }

                _connector?.Dispose();
                _connector = null;
            }

            _disposed = true;
        }
    }
}
