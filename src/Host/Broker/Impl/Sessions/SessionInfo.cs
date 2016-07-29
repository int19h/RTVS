﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.R.Host.Broker.Sessions {
    public class SessionInfo {
        public Guid Id { get; set; }

        public string InterpreterId { get; set; }
    }
}