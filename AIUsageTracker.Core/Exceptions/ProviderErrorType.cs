namespace AIUsageTracker.Core.Exceptions;

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
    InvalidResponseError,
}
