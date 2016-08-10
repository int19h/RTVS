﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Microsoft.R.Host.Broker.Logging {
    internal sealed class FileLogger : ILogger, IDisposable {
        private readonly string _category;
        private volatile StreamWriter _writer;

        public FileLogger(string category, StreamWriter writer) {
            _category = category;
            _writer = writer;
        }

        public void Dispose() {
            _writer = null;
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
            var writer = _writer;
            if (writer == null) {
                return;
            }

            string message = formatter(state, exception);
            lock (writer) {
                writer.WriteLine("[{0}]", _category);
                writer.WriteLine(message);
                writer.WriteLine();
                writer.Flush();
            }
        }
    }
}
