using System;

namespace AIUsageTracker.Core.Exceptions;

/// <summary>
/// Exception thrown when the provider configuration is invalid or missing.
/// </summary>
public class ProviderConfigurationException : ProviderException
{
    public ProviderConfigurationException(
        string providerId,
        string message = "Provider configuration is invalid or missing",
        Exception? innerException = null)
        : base(providerId, message, ProviderErrorType.ConfigurationError, innerException: innerException)
    {
    }
}

/// <summary>
/// Exception thrown when the provider returns invalid or malformed response data.
/// </summary>
public class ProviderResponseException : ProviderException
{
    public string? ResponseBody { get; }

    public ProviderResponseException(
        string providerId,
        string message = "Invalid response from provider",
        string? responseBody = null,
        Exception? innerException = null)
        : base(providerId, message, ProviderErrorType.InvalidResponseError, innerException: innerException)
    {
        ResponseBody = responseBody;
    }
}

/// <summary>
/// Exception thrown when JSON deserialization fails.
/// </summary>
public class ProviderDeserializationException : ProviderException
{
    public string? RawResponse { get; }

    public ProviderDeserializationException(
        string providerId,
        string message = "Failed to deserialize provider response",
        string? rawResponse = null,
        Exception? innerException = null)
        : base(providerId, message, ProviderErrorType.DeserializationError, innerException: innerException)
    {
        RawResponse = rawResponse;
    }
}
