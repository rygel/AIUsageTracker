// <copyright file="IResilientHttpClient.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Infrastructure.Http;

public interface IResilientHttpClient
{
    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken = default);

    Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, string policyName, CancellationToken cancellationToken = default);
}
