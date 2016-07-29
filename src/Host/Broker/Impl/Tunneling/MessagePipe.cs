// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.R.Host.Broker.Tunneling {
    internal class MessagePipe {
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

            public abstract Task<byte[]> ReadAsync();

            public abstract void Write(byte[] message);
        }

        private sealed class HostEnd : PipeEnd {
            private readonly MessagePipe _pipe;

            public HostEnd(MessagePipe pipe)
                : base(pipe, ref pipe._hostEnd) {
            }

            public override void Dispose() {
                throw new InvalidOperationException("Host end of the pipe should not be disposed.");
            }

            public override void Write(byte[] message) {
                _pipe._hostMessages.Enqueue(message);
            }

            public override async Task<byte[]> ReadAsync() {
                byte[] message;
                while (!_pipe._clientMessages.TryDequeue(out message)) {
                    await Task.Delay(100);
                }

                return message;
            }
        }

        private sealed class ClientEnd : PipeEnd {
            private readonly MessagePipe _pipe;
            private bool _isFirstRead = true;

            public ClientEnd(MessagePipe pipe)
                : base(pipe, ref pipe._clientEnd) {
            }

            public override void Dispose() {
                var unsent = new Queue<byte[]>(_pipe._sentPendingRequests.OrderBy(kv => kv.Key).Select(kv => kv.Value));
                _pipe._sentPendingRequests.Clear();
                Volatile.Write(ref _pipe._unsentPendingRequests, unsent);
                Volatile.Write(ref _pipe._clientEnd, null);
            }

            public override void Write(byte[] message) {
                ulong id, requestId;
                Parse(message, out id, out requestId);

                byte[] request;
                _pipe._sentPendingRequests.TryRemove(requestId, out request);

                _pipe._clientMessages.Enqueue(message);
            }

            public override async Task<byte[]> ReadAsync() {
                var handshake = _pipe._handshake;
                if (_isFirstRead) {
                    _isFirstRead = false;
                    if (handshake != null) {
                        return handshake;
                    }
                }

                byte[] message;
                if (_pipe._unsentPendingRequests.Count != 0) {
                    message = _pipe._unsentPendingRequests.Dequeue();
                } else {
                    while (!_pipe._hostMessages.TryDequeue(out message)) {
                        await Task.Delay(100);
                    }
                }

                ulong id, requestId;
                Parse(message, out id, out requestId);

                if (handshake == null) {
                    _pipe._handshake = message;
                } else if (requestId == ulong.MaxValue) {
                    _pipe._sentPendingRequests.TryAdd(id, message);
                }

                return message;
            }
        }

        public MessagePipe() {
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
    }
}
