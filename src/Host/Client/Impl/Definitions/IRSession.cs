// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.R.Host.Client {
    public interface IRSession : IRExpressionEvaluator, IRBlobService, IDisposable {
        event EventHandler<RBeforeRequestEventArgs> BeforeRequest;
        event EventHandler<RAfterRequestEventArgs> AfterRequest;
        event EventHandler<EventArgs> Mutated;
        event EventHandler<ROutputEventArgs> Output;
        event EventHandler<RConnectedEventArgs> Connected;
        event EventHandler<EventArgs> Interactive;
        event EventHandler<EventArgs> Disconnected;
        event EventHandler<EventArgs> Disposed;
        event EventHandler<EventArgs> DirectoryChanged;
        event EventHandler<EventArgs> PackagesInstalled;
        event EventHandler<EventArgs> PackagesRemoved;

        int Id { get; }
        string Prompt { get; }
        bool IsHostRunning { get; }
        Task HostStarted { get; }
        bool IsRemote { get; }
        bool RestartOnBrokerSwitch { get; set; }

        /// <summary>
        /// Whether the session is transient. A transient session is the one that doesn't contain any important state,
        /// and can be terminated or recycled without user confirmation.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The rule of thumb is that the session with which user interacts directly (e.g. via REPL) is non-transient,
        /// and all other helper sessions (for code completion, package management etc) are transient.
        /// </para>
        /// <para>
        /// Components utilizing transient sessions should assume that they can go away at any moment, and not treat
        /// it as an error. If the session was used for some computation, it should be silently restarted without 
        /// notifying the user.
        /// </para>
        /// </remarks>
        bool IsTransient { get; }

        Task<IRSessionInteraction> BeginInteractionAsync(bool isVisible = true, CancellationToken cancellationToken = default(CancellationToken));

        Task CancelAllAsync(CancellationToken cancellationToken = default(CancellationToken));
        Task StartHostAsync(RHostStartupInfo startupInfo, IRSessionCallback callback, int timeout = 3000, CancellationToken cancellationToken = default(CancellationToken));
        Task EnsureHostStartedAsync(RHostStartupInfo startupInfo, IRSessionCallback callback, int timeout = 3000, CancellationToken cancellationToken = default(CancellationToken));
        Task StopHostAsync(bool waitForShutdown = true, CancellationToken cancellationToken = default(CancellationToken));

        IDisposable DisableMutatedOnReadConsole();

        void FlushLog();
    }
}