// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Microsoft.R.Host.Broker.Logging {
    internal sealed class FileLoggerProvider : ILoggerProvider {
        private readonly StreamWriter _writer;

        public FileLoggerProvider()
            : this(GetLogFileName()) {
        }

        public FileLoggerProvider(string logFileName) {
            _writer = File.CreateText(logFileName);
        }

        private static string GetLogFileName() {
            return Path.Combine(Path.GetTempPath(), $@"Microsoft.R.Host.Broker_{DateTime.Now:yyyyMdd_HHmmss}_pid{Process.GetCurrentProcess().Id}.log");
        }

        public ILogger CreateLogger(string categoryName) {
            return new FileLogger(categoryName, _writer);
        }

        public void Dispose() {
            _writer.Dispose();
        }
    }
}
