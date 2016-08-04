// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Microsoft.R.Host.Broker.Logging {
    internal class FileLogger : ILogger {
        private readonly string _category;
        private readonly StreamWriter _writer;

        public FileLogger(string category, StreamWriter writer) {
            _category = category;
            _writer = writer;
        }

        private sealed class Scope : IDisposable {
            public void Dispose() {
            }
        }

        public IDisposable BeginScope<TState>(TState state) {
            return new Scope();
        }

        public bool IsEnabled(LogLevel logLevel) {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter) {
            string message = formatter(state, exception);
            lock (_writer) {
                _writer.WriteLine("[{0}]", _category);
                _writer.WriteLine(message);
                _writer.WriteLine();
                _writer.Flush();
            }
        }
    }
}
