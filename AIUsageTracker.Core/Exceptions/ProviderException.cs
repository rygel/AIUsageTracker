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
        this.ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
        this.ErrorType = errorType;
        this.HttpStatusCode = httpStatusCode;
    }
}

