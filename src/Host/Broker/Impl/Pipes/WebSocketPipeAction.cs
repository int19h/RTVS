﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.R.Host.Broker.Sessions;

namespace Microsoft.R.Host.Broker.Pipes {
    public class WebSocketPipeAction : IActionResult {
        private readonly Session _session;

        public WebSocketPipeAction(Session session) {
            _session = session;
        }

        public async Task ExecuteResultAsync(ActionContext actionContext) {
            var context = actionContext.HttpContext;
            var httpResponse = context.Features.Get<IHttpResponseFeature>();

            if (!context.WebSockets.IsWebSocketRequest) {
                httpResponse.ReasonPhrase = "Websocket connection expected";
                httpResponse.StatusCode = 401;
                return;
            }

            using (var socket = await context.WebSockets.AcceptWebSocketAsync("Microsoft.R.Host")) {
                var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                using (var pipe = _session.ConnectClient()) {
                    Task wsToPipe = WebSocketToPipeWorker(socket, pipe, cts.Token);
                    Task pipeToWs = PipeToWebSocketWorker(socket, pipe, cts.Token);
                    await Task.WhenAny(wsToPipe, pipeToWs).Unwrap();
                }

                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", cts.Token);
                cts.Cancel();
            }
        }

        private static async Task WebSocketToPipeWorker(WebSocket socket, IMessagePipeEnd pipe, CancellationToken cancellationToken) {
            const int blockSize = 0x10000;
            var buffer = new MemoryStream(blockSize);

            while (true) {
                cancellationToken.ThrowIfCancellationRequested();

                int index = (int)buffer.Length;
                buffer.SetLength(index + blockSize);

                var wsrr = await socket.ReceiveAsync(new ArraySegment<byte>(buffer.GetBuffer(), index, blockSize), cancellationToken);
                buffer.SetLength(index + wsrr.Count);

                if (wsrr.CloseStatus != null) {
                    break;
                } else if (wsrr.EndOfMessage) {
                    pipe.Write(buffer.ToArray());
                    buffer.SetLength(0);
                }
            }
        }

        private static async Task PipeToWebSocketWorker(WebSocket socket, IMessagePipeEnd pipe, CancellationToken cancellationToken) {
            while (true) {
                cancellationToken.ThrowIfCancellationRequested();

                byte[] message;
                try {
                    message = await pipe.ReadAsync(cancellationToken);
                } catch (HostDisconnectedException) {
                    break;
                }

                await socket.SendAsync(new ArraySegment<byte>(message, 0, message.Length), WebSocketMessageType.Binary, true, cancellationToken);
            }
        }
    }
}
