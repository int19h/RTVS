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
        private static readonly Uri GetUri = new Uri("/sessions", UriKind.Relative);
        private static readonly UriTemplate SessionUri = new UriTemplate("/sessions/{id}");
        private static readonly UriTemplate SessionDeleteUri = new UriTemplate("/sessions/{id}?isGraceful={isGraceful}&saveRData={saveRData}");

        public SessionsWebService(HttpClient httpClient, ICredentialsDecorator credentialsDecorator)
            : base(httpClient, credentialsDecorator) {
        }

        public Task<IEnumerable<SessionInfo>> GetAsync(CancellationToken cancellationToken = default(CancellationToken)) =>
            HttpGetAsync<IEnumerable<SessionInfo>>(GetUri, cancellationToken);

        public Task<SessionInfo> PutAsync(string id, SessionCreateRequest request, CancellationToken cancellationToken = default(CancellationToken)) =>
            HttpPutAsync<SessionCreateRequest, SessionInfo>(SessionUri, request, cancellationToken, id);

        public Task DeleteAsync(string id, bool isGraceful, bool saveRData, CancellationToken cancellationToken = default(CancellationToken)) =>
            HttpDeleteAsync(SessionDeleteUri, cancellationToken, id, isGraceful, saveRData);
    }
}
