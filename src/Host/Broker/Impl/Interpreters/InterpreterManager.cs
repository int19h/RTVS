// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using Microsoft.Extensions.Options;
using Microsoft.R.Interpreters;

namespace Microsoft.R.Host.Broker.Interpreters {
    public class InterpreterManager {
        private readonly IOptions<InterpretersOptions> _interpOptions;
        private readonly RInstallation _rInstallation = new RInstallation();

        public readonly IReadOnlyCollection<InterpreterInfo> Interpreters;

        [ImportingConstructor]
        public InterpreterManager(IOptions<InterpretersOptions> interpOptions) {
            _interpOptions = interpOptions;
            Interpreters = GetInterpreters().ToArray();
        }

        private IEnumerable<InterpreterInfo> GetInterpreters() {
            if (_interpOptions.Value.AutoDetect) {
                var detectedInfo = GetInterpreterInfo("", null, throwOnError: false);
                if (detectedInfo != null) {
                    yield return detectedInfo;
                }
            }

            foreach (var kv in _interpOptions.Value.Interpreters) {
                yield return GetInterpreterInfo(kv.Key, kv.Value.BasePath, throwOnError: true);
            }
        }

        private InterpreterInfo GetInterpreterInfo(string name, string basePath, bool throwOnError) {
            var rid = _rInstallation.GetInstallationData(basePath, new SupportedRVersionRange());
            if (rid.Status != RInstallStatus.OK) {
                if (throwOnError) {
                    throw rid.Exception ?? new InvalidOperationException("Failed to retrieve R installation data");
                } else {
                    return null;
                }
            }

            return new InterpreterInfo {
                Id = name,
                Path = rid.Path,
                BinPath = rid.BinPath,
                Version = rid.Version,
            };
        }
    }
}

