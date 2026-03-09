// <copyright file="ProviderAuthenticationException.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Exceptions
{
    using System;

    /// <summary>
    /// Exception thrown when provider authentication fails (401 Unauthorized).
    /// </summary>
    public class ProviderAuthenticationException : ProviderException
    {
        public ProviderAuthenticationException(
            string providerId,
            string message = "Authentication failed - please check your API key",
            Exception? innerException = null)
            : base(providerId, message, ProviderErrorType.AuthenticationError, 401, innerException)
        {
        }
    }
}
