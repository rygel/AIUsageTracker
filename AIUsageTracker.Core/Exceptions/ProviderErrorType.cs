// <copyright file="ProviderErrorType.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Exceptions
{
    /// <summary>
    /// Specific error types that can occur during provider operations.
    /// </summary>
    public enum ProviderErrorType
    {
        Unknown,
        ConfigurationError,
        AuthenticationError,
        AuthorizationError,
        NetworkError,
        TimeoutError,
        RateLimitError,
        ServerError,
        DeserializationError,
        InvalidResponseError,
    }
}
