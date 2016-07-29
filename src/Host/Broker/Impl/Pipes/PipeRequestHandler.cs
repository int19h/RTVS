// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebSockets.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.R.Host.Broker.Sessions;

namespace Microsoft.R.Host.Broker.Pipes {
    public class PipeRequestHandler {
        private readonly SessionManager _sessionManager;

        [ImportingConstructor]
        public PipeRequestHandler(SessionManager sessionManager) {
            _sessionManager = sessionManager;
        }

        public async Task HandleRequest(HttpContext context, bool isHost) {
            var httpResponse = context.Features.Get<IHttpResponseFeature>();

            if (!context.WebSockets.IsWebSocketRequest) {
                httpResponse.ReasonPhrase = "Websocket connection expected";
                httpResponse.StatusCode = 401;
                return;
            }

            var id = Guid.Parse((string)context.GetRouteValue("id"));

            var session = _sessionManager.GetSession(id);
            var pipe = isHost ? session.ConnectHost() : session.ConnectClient();

            //string key = string.Join(", ", context.Request.Headers[Constants.Headers.SecWebSocketKey]);
            //var responseHeaders = HandshakeHelpers.GenerateResponseHeaders(key, "Microsoft.R.Host");
            //foreach (var header in responseHeaders) {
            //    context.Response.Headers[header.Key] = header.Value;
            //}
            //var upgrade = context.Features.Get<IHttpUpgradeFeature>();
            //var stream = await upgrade.UpgradeAsync();
            //await Task.Delay(1000);
            //await stream.FlushAsync();

            var socket = await context.WebSockets.AcceptWebSocketAsync("Microsoft.R.Host");

            Task wsToPipe = WebSocketToPipeWorker(socket, pipe);
            Task pipeToWs = PipeToWebSocketWorker(socket, pipe);

            await Task.WhenAll(wsToPipe, pipeToWs);
        }

        private static async Task WebSocketToPipeWorker(WebSocket socket, IMessagePipeEnd pipe) {
            var cancellationToken = new CancellationToken();

            const int blockSize = 0x10000;
            var buffer = new MemoryStream(blockSize);

            while (true) {
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

        private static async Task PipeToWebSocketWorker(WebSocket socket, IMessagePipeEnd pipe) {
            var cancellationToken = new CancellationToken();

            while (true) {
                var message = await pipe.ReadAsync();
                await socket.SendAsync(new ArraySegment<byte>(message, 0, message.Length), WebSocketMessageType.Binary, true, cancellationToken);
            }
        }

    }
}
