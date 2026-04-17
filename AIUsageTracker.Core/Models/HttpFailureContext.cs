// <copyright file="HttpFailureContext.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Models;

/// <summary>
/// Carries structured context about an HTTP/transport failure.
/// Answers "what kind of failure happened?" without prescribing how each layer should react.
/// Each layer (provider usage synthesis, monitor resilience, monitor-client retry) applies
/// its own projection policy on top of this shared context.
/// </summary>
public sealed class HttpFailureContext
{
    /// <summary>Gets the classification of the failure kind.</summary>
    public HttpFailureClassification Classification { get; init; } = HttpFailureClassification.Unknown;

    /// <summary>Gets the HTTP status code, when the failure originated from an HTTP response.</summary>
    public int? HttpStatus { get; init; }

    /// <summary>
    /// Gets a user-safe message describing the failure.
    /// Must not contain raw exception messages or stack traces.
    /// </summary>
    public string UserMessage { get; init; } = string.Empty;

    /// <summary>
    /// Gets the server-requested retry delay, when the upstream service provided one (e.g. Retry-After header).
    /// Null when no retry delay was signalled.
    /// </summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>
    /// Gets a value indicating whether the failure is likely transient and safe to retry.
    /// True for network errors, timeouts, rate limits, and 5xx server errors.
    /// False for auth failures, client errors, and deserialization errors.
    /// </summary>
    public bool IsLikelyTransient { get; init; }

    /// <summary>
    /// Gets the fully qualified exception type name, when the failure originated from a caught exception.
    /// Intended for diagnostic logging only — not shown in UI.
    /// </summary>
    public string? ExceptionTypeName { get; init; }

    /// <summary>
    /// Gets an optional free-form diagnostic note for internal logging.
    /// Not shown in UI and must not contain user credentials or secrets.
    /// </summary>
    public string? DiagnosticNote { get; init; }

    /// <summary>
    /// Creates an <see cref="HttpFailureContext"/> from an HTTP status code using standard classification rules.
    /// </summary>
    /// <returns></returns>
    public static HttpFailureContext FromHttpStatus(int httpStatus, string userMessage = "")
    {
        var classification = httpStatus switch
        {
            401 => HttpFailureClassification.Authentication,
            403 => HttpFailureClassification.Authorization,
            429 => HttpFailureClassification.RateLimit,
            >= 500 and <= 599 => HttpFailureClassification.Server,
            >= 400 and <= 499 => HttpFailureClassification.Client,
            _ => HttpFailureClassification.Unknown,
        };

        var isLikelyTransient = classification is
            HttpFailureClassification.RateLimit or
            HttpFailureClassification.Server;

        return new HttpFailureContext
        {
            Classification = classification,
            HttpStatus = httpStatus,
            UserMessage = userMessage,
            IsLikelyTransient = isLikelyTransient,
        };
    }

    /// <summary>
    /// Creates an <see cref="HttpFailureContext"/> for a network or timeout failure.
    /// </summary>
    /// <returns></returns>
    public static HttpFailureContext FromException(Exception exception, HttpFailureClassification classification, string userMessage = "")
    {
        ArgumentNullException.ThrowIfNull(exception);

        return new HttpFailureContext
        {
            Classification = classification,
            UserMessage = userMessage,
            IsLikelyTransient = classification is
                HttpFailureClassification.Network or
                HttpFailureClassification.Timeout,
            ExceptionTypeName = exception.GetType().FullName,
        };
    }
}
