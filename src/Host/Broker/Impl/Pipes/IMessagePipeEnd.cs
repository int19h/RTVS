﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Threading.Tasks;

namespace Microsoft.R.Host.Broker.Pipes {
    public interface IMessagePipeEnd : IDisposable {
        Task<byte[]> ReadAsync();
        void Write(byte[] message);
    }
}
