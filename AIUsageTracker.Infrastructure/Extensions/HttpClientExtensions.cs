// <copyright file="HttpClientExtensions.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using Microsoft.Extensions.DependencyInjection;

namespace AIUsageTracker.Infrastructure.Extensions;

public static class HttpClientExtensions
{
    public static IServiceCollection AddConfiguredHttpClients(this IServiceCollection services)
    {
        // Default HttpClient for general use
        services.AddHttpClient(string.Empty);

        // Plain client for providers that handle retries themselves
        services.AddHttpClient("PlainClient");

        // Short-timeout client for localhost API calls (e.g. AntigravityProvider)
        services.AddHttpClient("LocalhostClient")
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(1.5));

        return services;
    }
}
