// <copyright file="ProviderConfigurationException.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Exceptions
{
    /// <summary>
    /// Exception thrown when the provider configuration is invalid or missing.
    /// </summary>
    public class ProviderConfigurationException : ProviderException
    {
        public ProviderConfigurationException(
            string providerId,
            string message = "Provider configuration is invalid or missing",
            System.Exception? innerException = null)
            : base(providerId, message, ProviderErrorType.ConfigurationError, innerException: innerException)
        {
        }
    }
}
