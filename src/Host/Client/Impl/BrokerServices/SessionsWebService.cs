﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.R.Host.Protocol;

namespace Microsoft.R.Host.Client.BrokerServices {
    public class SessionsWebService : WebService, ISessionsWebService {
        public SessionsWebService(HttpClient httpClient, ICredentialsProvider credentialsProvider)
            : base(httpClient, credentialsProvider) {
        }

        private static readonly Uri getUri = new Uri("/sessions", UriKind.Relative);

        public Task<IEnumerable<SessionInfo>> GetAsync(CancellationToken cancellationToken = default(CancellationToken)) =>
            HttpGetAsync<IEnumerable<SessionInfo>>(getUri, cancellationToken);

        private static readonly UriTemplate sessionUri = new UriTemplate("/sessions/{name}");

        public Task<SessionInfo> PutAsync(string id, SessionCreateRequest request, CancellationToken cancellationToken = default(CancellationToken)) =>
            HttpPutAsync<SessionCreateRequest, SessionInfo>(sessionUri, request, cancellationToken, id);

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default(CancellationToken)) =>
            HttpDeleteAsync(sessionUri, cancellationToken, id);
    }
}
