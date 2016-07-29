using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.R.Host.Broker.Tunneling {
    internal class RHostPipe {
        //private readonly object _lock = new object();
        private readonly ConcurrentQueue<byte[]> _hostMessages = new ConcurrentQueue<byte[]>();
        private readonly ConcurrentQueue<byte[]> _clientMessages = new ConcurrentQueue<byte[]>();

        private ConcurrentDictionary<ulong, byte[]> _sentPendingRequests = new ConcurrentDictionary<ulong, byte[]>();
        private Queue<byte[]> _unsentPendingRequests = new Queue<byte[]>();

        private byte[] _handshake;
        private ClientEnd _client;

        public sealed class HostEnd {
            private readonly RHostPipe _pipe;

            public HostEnd(RHostPipe pipe) {
                if (pipe.Host != null) {
                    throw new InvalidOperationException("Pipe already has a host end");
                }
            }

            public void Write(byte[] message) {
                _pipe._hostMessages.Enqueue(message);
            }

            public async Task<byte[]> ReadAsync() {
                byte[] message;
                while (!_pipe._clientMessages.TryDequeue(out message)) {
                    await Task.Delay(100);
                }

                return message;
            }
        }

        public sealed class ClientEnd : IDisposable {
            private readonly RHostPipe _pipe;
            private bool _isFirstRead = true;

            public ClientEnd(RHostPipe pipe) {
                if (Interlocked.CompareExchange(ref pipe._client, this, null) != null) {
                    throw new InvalidOperationException("Pipe already has a client end");
                }

                _pipe = pipe;
            }

            public void Dispose() {
                var unsent = new Queue<byte[]>(_pipe._sentPendingRequests.OrderBy(kv => kv.Key).Select(kv => kv.Value));
                _pipe._sentPendingRequests.Clear();
                Volatile.Write(ref _pipe._unsentPendingRequests, unsent);
                Volatile.Write(ref _pipe._client, null);
            }

            public void Write(byte[] message) {
                ulong id, requestId;
                Parse(message, out id, out requestId);

                byte[] request;
                _pipe._sentPendingRequests.TryRemove(requestId, out request);

                _pipe._clientMessages.Enqueue(message);
            }

            public async Task<byte[]> ReadAsync() {
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

        public HostEnd Host { get; }

        public RHostPipe() {
            Host = new HostEnd(this);
        }

        public ClientEnd ConnectClient() {
            return new ClientEnd(this);
        }

        private static void Parse(byte[] message, out ulong id, out ulong requestId) {
            id = BitConverter.ToUInt64(message, 0);
            requestId = BitConverter.ToUInt64(message, 8);
        }
    }
}
