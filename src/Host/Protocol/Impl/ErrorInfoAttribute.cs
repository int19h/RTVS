// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using Microsoft.R.Host.Protocol;

namespace Microsoft.R.Host.Protocol {
    [AttributeUsage(AttributeTargets.Field)]
    public class ErrorInfoAttribute : Attribute {
        public Type ErrorInfoType { get; }

        public ErrorInfoAttribute(Type errorInfoType) {
            ErrorInfoType = errorInfoType;
        }
    }
}
