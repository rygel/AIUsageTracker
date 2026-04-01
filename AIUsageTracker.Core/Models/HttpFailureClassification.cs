// <copyright file="HttpFailureClassification.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models;

/// <summary>
/// Classifies the kind of HTTP/transport failure that occurred.
/// Used as a shared classification model across provider, monitor resilience,
/// and monitor-client layers without collapsing their distinct projection responsibilities.
/// </summary>
public enum HttpFailureClassification
{
    /// <summary>Classification could not be determined.</summary>
    Unknown = 0,

    /// <summary>Request was rejected due to missing or invalid credentials (HTTP 401).</summary>
    Authentication = 1,

    /// <summary>Request was rejected due to insufficient permissions (HTTP 403).</summary>
    Authorization = 2,

    /// <summary>Request was throttled by the upstream service (HTTP 429).</summary>
    RateLimit = 3,

    /// <summary>A network-level failure prevented the request from reaching the server.</summary>
    Network = 4,

    /// <summary>The request or response did not complete within the allowed time window.</summary>
    Timeout = 5,

    /// <summary>The upstream server returned a 5xx error response.</summary>
    Server = 6,

    /// <summary>The caller sent an invalid request and received a 4xx response (excluding 401/403/429).</summary>
    Client = 7,

    /// <summary>The response could not be parsed or did not match the expected schema.</summary>
    Deserialization = 8,
}
