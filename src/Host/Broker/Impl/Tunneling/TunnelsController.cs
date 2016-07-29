// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net.WebSockets;
using System.Net;
using System.Threading;
using System.IO;
using Microsoft.R.Host.Broker.Sessions;

namespace Microsoft.R.Host.Broker.Tunneling {
    //[Authorize]
    [Route("/tunnels")]
    public class TunnelsController : Controller {
        private readonly SessionManager _sessionManager;

        public TunnelsController(SessionManager sessionManager) {
            _sessionManager = sessionManager;
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(Guid id) {
            if (HttpContext.Connection.RemoteIpAddress == null && !IPAddress.IsLoopback(HttpContext.Connection.RemoteIpAddress)) {
                return Forbid();
            }

            if (!HttpContext.WebSockets.IsWebSocketRequest) {
                return BadRequest("Websocket connection expected");
            }

            var session = _sessionManager.GetSession(id);
            var pipe = session.ConnectHost();

            var socket = await HttpContext.WebSockets.AcceptWebSocketAsync();

            Task wsToPipe = WebSocketToPipeWorker(socket, pipe);
            Task pipeToWs = PipeToWebSocketWorker(socket, pipe);

            await Task.WhenAll(wsToPipe, pipeToWs);
            return new EmptyResult();
        }

        private async Task WebSocketToPipeWorker(WebSocket socket, IMessagePipeEnd pipe) {
            var cancellationToken = new CancellationToken();

            const int blockSize = 0x10000;
            var stream = new MemoryStream(blockSize);

            while (true) {
                int index = (int)stream.Length;
                stream.SetLength(stream.Length + blockSize);
                var buf = stream.GetBuffer();

                var wsrr = await socket.ReceiveAsync(new ArraySegment<byte>(buf, index, blockSize), cancellationToken);
                if (wsrr.CloseStatus != null) {
                    break;
                } else if (wsrr.EndOfMessage) {
                    pipe.Write(stream.ToArray());
                    stream.SetLength(0);
                }
            }
        }

        private async Task PipeToWebSocketWorker(WebSocket socket, IMessagePipeEnd pipe) {
            var cancellationToken = new CancellationToken();

            while (true) {
                var message = await pipe.ReadAsync();
                await socket.SendAsync(new ArraySegment<byte>(message, 0, message.Length), WebSocketMessageType.Binary, true, cancellationToken);
            }
        }
    }
}
