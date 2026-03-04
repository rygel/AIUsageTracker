using System;
using System.Net;

namespace AIUsageTracker.Core.Exceptions;

/// <summary>
/// Exception thrown when a network error occurs (connection failure, DNS resolution, etc.).
/// </summary>
public class ProviderNetworkException : ProviderException
{
    public ProviderNetworkException(
        string providerId,
        string message = "Network error - please check your connection",
        Exception? innerException = null)
        : base(providerId, message, ProviderErrorType.NetworkError, innerException: innerException)
    {
    }
}

/// <summary>
/// Exception thrown when a request times out.
/// </summary>
public class ProviderTimeoutException : ProviderException
{
    public TimeSpan TimeoutDuration { get; }

    public ProviderTimeoutException(
        string providerId,
        TimeSpan timeoutDuration,
        string message = "Request timed out",
        Exception? innerException = null)
        : base(providerId, message, ProviderErrorType.TimeoutError, innerException: innerException)
    {
        TimeoutDuration = timeoutDuration;
    }
}

/// <summary>
/// Exception thrown when rate limiting is encountered (429 Too Many Requests).
/// </summary>
public class ProviderRateLimitException : ProviderException
{
    public DateTime? RetryAfter { get; }

    public ProviderRateLimitException(
        string providerId,
        DateTime? retryAfter = null,
        string message = "Rate limit exceeded - please wait before retrying",
        Exception? innerException = null)
        : base(providerId, message, ProviderErrorType.RateLimitError, 429, innerException)
    {
        RetryAfter = retryAfter;
    }
}

/// <summary>
/// Exception thrown when the provider server returns an error (500+ status codes).
/// </summary>
public class ProviderServerException : ProviderException
{
    public ProviderServerException(
        string providerId,
        int httpStatusCode,
        string message = "Provider server error",
        Exception? innerException = null)
        : base(providerId, message, ProviderErrorType.ServerError, httpStatusCode, innerException)
    {
    }
}
