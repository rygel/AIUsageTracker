// <copyright file="ProviderServerException.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Exceptions
{
    /// <summary>
    /// Exception thrown when the provider server returns an error (500+ status codes).
    /// </summary>
    public class ProviderServerException : ProviderException
    {
        public ProviderServerException(
            string providerId,
            int httpStatusCode,
            string message = "Provider server error",
            System.Exception? innerException = null)
            : base(providerId, message, ProviderErrorType.ServerError, httpStatusCode, innerException)
        {
        }
    }
}
