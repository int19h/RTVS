﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.R.Host.Client.Host;
using Microsoft.R.Interpreters;
using Microsoft.R.Host.Client.Install;
using Microsoft.R.Host.Client.Session;
using Microsoft.UnitTests.Core.Threading;
using Microsoft.UnitTests.Core.XUnit;
using Microsoft.UnitTests.Core.XUnit.MethodFixtures;
using Xunit;

namespace Microsoft.R.Host.Client.Test.Session {
    public partial class RSessionTest {
        public class CancelAll : IAsyncLifetime {
            private readonly TaskObserverMethodFixture _taskObserver;
            private readonly MethodInfo _testMethod;
            private readonly IRHostBrokerConnector _brokerConnector  = new RHostBrokerConnector(name: nameof(CancelAll));
            private readonly RSession _session;

            public CancelAll(TestMethodFixture testMethod, TaskObserverMethodFixture taskObserver) {
                _taskObserver = taskObserver;
                _testMethod = testMethod.MethodInfo;
                _session = new RSession(0, _brokerConnector, () => {});
            }

            public async Task InitializeAsync() {
                await _session.StartHostAsync(new RHostStartupInfo {
                    Name = _testMethod.Name
                }, null, 50000);

                _taskObserver.ObserveTaskFailure(_session.RHost.GetRHostRunTask());
            }

            public async Task DisposeAsync() {
                await _session.StopHostAsync();
                _session.Dispose();
                _brokerConnector.Dispose();
            }

            [Test]
            [Category.R.Session]
            public async Task CancelAllInParallel() {
                Task responceTask;
                using (var interaction = await _session.BeginInteractionAsync()) {
                    responceTask = interaction.RespondAsync("while(TRUE){}\n");
                }

                await ParallelTools.InvokeAsync(4, i => _session.CancelAllAsync());

                _session.IsHostRunning.Should().BeTrue();
                responceTask.Status.Should().Be(TaskStatus.Canceled);
            }
        }
    }
}
