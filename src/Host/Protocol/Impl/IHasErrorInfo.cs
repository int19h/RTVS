// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Runtime.Serialization;

namespace Microsoft.R.Host.Protocol {
    public interface IHasErrorInfo {
        void InspectErrorInfo(IErrorInfoInspector inspector);
    }
}
