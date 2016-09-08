// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Text;
using Microsoft.Common.Core.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.R.Host.Protocol;
using Microsoft.R.Interpreters;

namespace Microsoft.R.Host.Broker.Interpreters {
    public class InterpreterManager {
        private const string _localId = "local";

        private readonly ROptions _options;
        private readonly ILogger _logger;
        private IFileSystem _fs;

        public IReadOnlyCollection<Interpreter> Interpreters { get; private set; }

        [ImportingConstructor]
        public InterpreterManager(IOptions<ROptions> options, ILogger<InterpreterManager> logger, IFileSystem fs) {
            _options = options.Value;
            _logger = logger;
            _fs = fs;
        }

        public void Initialize() {
            Interpreters = GetInterpreters().ToArray();

            var sb = new StringBuilder($"{Interpreters.Count} interpreters configured:");
            foreach (var interp in Interpreters) {
                sb.Append(Environment.NewLine + $"'{interp.Id}': {interp.Version} at \"{interp.Path}\"");
            }
            _logger.LogInformation(sb.ToString());
        }

        private IEnumerable<Interpreter> GetInterpreters() {
            if (_options.AutoDetect) {
                _logger.LogTrace("Auto-detecting R ...");

                var engines = new RInstallation().GetCompatibleEngines();
                if (engines.Any()) {
                    foreach (var e in engines) {
                        var detected = new Interpreter(this, Guid.NewGuid().ToString(), e.Name, e.InstallPath, e.BinPath, e.Version);
                        _logger.LogTrace($"R {detected.Version} detected at \"{detected.Path}\".");
                        yield return detected;
                    }
                } else {
                    _logger.LogWarning("No R interpreters auto-detected.");
                }
            }

            foreach (var kv in _options.Interpreters) {
                string id = kv.Key;
                InterpreterOptions options = kv.Value;

                if (!string.IsNullOrEmpty(options.BasePath) && _fs.DirectoryExists(options.BasePath)) {
                    var interpInfo = new RInterpreterInfo(string.Empty, options.BasePath);
                    if (interpInfo.VerifyInstallation()) {
                        yield return new Interpreter(this, id, options.Name, interpInfo.InstallPath, interpInfo.BinPath, interpInfo.Version);
                        continue;
                    }
                }

                _logger.LogError($"Failed to retrieve R installation data for interpreter \"{id}\" at \"{options.BasePath}\"");
            }
        }

        /// <summary>
        /// Returns an interpreter for a given ID. If ID is null or an empty string, returns the first available interpreter.
        /// </summary>
        /// <exception cref="InterpreterNotFoundException">
        /// Interpreter with the given ID was not found. If ID was null or an empty string, this indicates that no interpreters are available.
        /// </exception>
        public Interpreter GetInterpreter(string id = null) {
            var result = string.IsNullOrEmpty(id)
                ? Interpreters.FirstOrDefault()
                : Interpreters.FirstOrDefault(interp => interp.Id == id);

            if (result != null) {
                return result;
            } else {
                throw new InterpreterNotFoundException(id);
            }
        }
    }
}

