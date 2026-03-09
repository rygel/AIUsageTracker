// <copyright file="ProviderResponseException.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Exceptions
{
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
            System.Exception? innerException = null)
            : base(providerId, message, ProviderErrorType.InvalidResponseError, innerException: innerException)
        {
            this.ResponseBody = responseBody;
        }
    }
}
