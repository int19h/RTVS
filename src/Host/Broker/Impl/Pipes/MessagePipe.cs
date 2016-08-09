// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.R.Host.Client;

namespace Microsoft.R.Host.Broker.Pipes {
    public class MessagePipe {
        private readonly ILogger _logger;
        private readonly ConcurrentQueue<byte[]> _hostMessages = new ConcurrentQueue<byte[]>();
        private readonly ConcurrentQueue<byte[]> _clientMessages = new ConcurrentQueue<byte[]>();

        private ConcurrentDictionary<ulong, byte[]> _sentPendingRequests = new ConcurrentDictionary<ulong, byte[]>();
        private Queue<byte[]> _unsentPendingRequests = new Queue<byte[]>();

        private byte[] _handshake;
        private IMessagePipeEnd _hostEnd, _clientEnd;

        private abstract class PipeEnd : IMessagePipeEnd {
            protected MessagePipe Pipe { get; }

            protected PipeEnd(MessagePipe pipe, ref IMessagePipeEnd end) {
                Pipe = pipe;
                if (Interlocked.CompareExchange(ref end, this, null) != null) {
                    throw new InvalidOperationException($"Pipe already has a {GetType().Name}");
                }
            }

            public abstract void Dispose();

            public abstract Task<byte[]> ReadAsync(CancellationToken cancellationToken);

            public abstract void Write(byte[] message);
        }

        private sealed class HostEnd : PipeEnd {
            public HostEnd(MessagePipe pipe)
                : base(pipe, ref pipe._hostEnd) {
            }

            public override void Dispose() {
                throw new InvalidOperationException("Host end of the pipe should not be disposed.");
            }

            public override void Write(byte[] message) {
                Pipe._hostMessages.Enqueue(message);
            }

            public override async Task<byte[]> ReadAsync(CancellationToken cancellationToken) {
                byte[] message;

                int i = 0;
                while (!Pipe._clientMessages.TryDequeue(out message)) {
                    //await Task.Delay(100, cancellationToken);
                    if (++i % 1000 == 0) await Task.Yield();
                }

                return message;
            }
        }

        private sealed class ClientEnd : PipeEnd {
            private bool _isFirstRead = true;

            public ClientEnd(MessagePipe pipe)
                : base(pipe, ref pipe._clientEnd) {
            }

            public override void Dispose() {
                var unsent = new Queue<byte[]>(Pipe._sentPendingRequests.OrderBy(kv => kv.Key).Select(kv => kv.Value));
                Pipe._sentPendingRequests.Clear();
                Volatile.Write(ref Pipe._unsentPendingRequests, unsent);
                Volatile.Write(ref Pipe._clientEnd, null);
            }

            public override void Write(byte[] message) {
                ulong id, requestId;
                Parse(message, out id, out requestId);

                byte[] request;
                Pipe._sentPendingRequests.TryRemove(requestId, out request);

                Pipe._clientMessages.Enqueue(message);
            }

            public override async Task<byte[]> ReadAsync(CancellationToken cancellationToken) {
                var handshake = Pipe._handshake;
                if (_isFirstRead) {
                    _isFirstRead = false;
                    if (handshake != null) {
                        return handshake;
                    }
                }

                byte[] message;
                if (Pipe._unsentPendingRequests.Count != 0) {
                    message = Pipe._unsentPendingRequests.Dequeue();
                } else {
                    int i = 0;
                    while (!Pipe._hostMessages.TryDequeue(out message)) {
                        //await Task.Delay(100, cancellationToken);
                        if (++i % 1000 == 0) await Task.Yield();
                    }
                }

                ulong id, requestId;
                Parse(message, out id, out requestId);

                if (handshake == null) {
                    Pipe._handshake = message;
                } else if (requestId == ulong.MaxValue) {
                    Pipe._sentPendingRequests.TryAdd(id, message);
                }

                return message;
            }
        }

        public MessagePipe(ILogger logger) {
            _logger = logger;
        }

        public IMessagePipeEnd ConnectHost() {
            return new HostEnd(this);
        }

        public IMessagePipeEnd ConnectClient() {
            return new ClientEnd(this);
        }

        private static void Parse(byte[] message, out ulong id, out ulong requestId) {
            id = BitConverter.ToUInt64(message, 0);
            requestId = BitConverter.ToUInt64(message, 8);
        }

        private void LogMessage(string prefix, byte[] messageData) {
            if (_logger == null) {
                return;
            }

            var message = new Message(messageData);
            var sb = new StringBuilder($"{prefix} #{message.Id}# {message.Name} ");

            if (message.IsResponse) {
                sb.Append($"#{message.RequestId}# ");
            }

            sb.Append(message.Json);

            if (message.Blob != null && message.Blob.Length != 0) {
                sb.Append($" <raw ({message.Blob.Length} bytes)>");
            }

            _logger.LogTrace(sb.ToString());
        }
    }
}
