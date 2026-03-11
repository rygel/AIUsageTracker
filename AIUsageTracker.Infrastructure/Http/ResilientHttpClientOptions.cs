// <copyright file="ResilientHttpClientOptions.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;

namespace AIUsageTracker.Infrastructure.Http;

public class ResilientHttpClientOptions
{
    public int MaxRetryCount { get; set; } = 3;

    public double BackoffBase { get; set; } = 2;

    public TimeSpan CircuitBreakerDuration { get; set; } = TimeSpan.FromSeconds(30);

    public int CircuitBreakerFailureThreshold { get; set; } = 5;

    public IReadOnlyList<HttpStatusCode> RetryStatusCodes { get; set; } =
    [
        HttpStatusCode.RequestTimeout,
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout,
    ];

    public IReadOnlyList<HttpStatusCode> CircuitBreakerStatusCodes { get; set; } =
    [
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout,
    ];
}
