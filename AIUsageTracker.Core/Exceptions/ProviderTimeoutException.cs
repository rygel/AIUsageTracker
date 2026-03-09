// <copyright file="ProviderTimeoutException.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Exceptions
{
    /// <summary>
    /// Exception thrown when a request times out.
    /// </summary>
    public class ProviderTimeoutException : ProviderException
    {
        public System.TimeSpan TimeoutDuration { get; }

        public ProviderTimeoutException(
            string providerId,
            System.TimeSpan timeoutDuration,
            string message = "Request timed out",
            System.Exception? innerException = null)
            : base(providerId, message, ProviderErrorType.TimeoutError, innerException: innerException)
        {
            this.TimeoutDuration = timeoutDuration;
        }
    }
}
