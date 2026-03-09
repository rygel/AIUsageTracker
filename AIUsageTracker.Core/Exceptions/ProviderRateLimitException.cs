// <copyright file="ProviderRateLimitException.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Exceptions
{
    /// <summary>
    /// Exception thrown when rate limiting is encountered (429 Too Many Requests).
    /// </summary>
    public class ProviderRateLimitException : ProviderException
    {
        public System.DateTime? RetryAfter { get; }

        public ProviderRateLimitException(
            string providerId,
            System.DateTime? retryAfter = null,
            string message = "Rate limit exceeded - please wait before retrying",
            System.Exception? innerException = null)
            : base(providerId, message, ProviderErrorType.RateLimitError, 429, innerException)
        {
            this.RetryAfter = retryAfter;
        }
    }
}
