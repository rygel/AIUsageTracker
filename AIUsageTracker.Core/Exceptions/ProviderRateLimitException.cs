// <copyright file="ProviderRateLimitException.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System;

namespace AIUsageTracker.Core.Exceptions;

/// <summary>
/// Exception thrown when rate limiting is encountered (429 Too Many Requests).
/// </summary>
public class ProviderRateLimitException : ProviderException
{
    public ProviderRateLimitException(
        string providerId,
        DateTime? retryAfter = null,
        string message = "Rate limit exceeded - please wait before retrying",
        Exception? innerException = null)
        : base(providerId, message, ProviderErrorType.RateLimitError, 429, innerException)
    {
        this.RetryAfter = retryAfter;
    }

    public DateTime? RetryAfter { get; }
}
