// <copyright file="ProviderDeserializationException.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Exceptions
{
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
            System.Exception? innerException = null)
            : base(providerId, message, ProviderErrorType.DeserializationError, innerException: innerException)
        {
            this.RawResponse = rawResponse;
        }
    }
}
