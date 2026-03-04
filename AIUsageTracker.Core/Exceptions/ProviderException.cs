using System;

namespace AIUsageTracker.Core.Exceptions;

/// <summary>
/// Base exception for all provider-related errors.
/// </summary>
public class ProviderException : Exception
{
    public string ProviderId { get; }
    public ProviderErrorType ErrorType { get; }
    public int? HttpStatusCode { get; }

    public ProviderException(
        string providerId,
        string message,
        ProviderErrorType errorType = ProviderErrorType.Unknown,
        int? httpStatusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        ErrorType = errorType;
        HttpStatusCode = httpStatusCode;
    }
}

/// <summary>
/// Specific error types that can occur during provider operations.
/// </summary>
public enum ProviderErrorType
{
    Unknown,
    ConfigurationError,
    AuthenticationError,
    AuthorizationError,
    NetworkError,
    TimeoutError,
    RateLimitError,
    ServerError,
    DeserializationError,
    InvalidResponseError
}
