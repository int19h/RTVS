﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.R.Host.Client {
    public interface IMessageTransport {
        Task SendAsync(Message message, CancellationToken ct = default(CancellationToken));
        Task<Message> ReceiveAsync(CancellationToken ct = default(CancellationToken));
    }
}
