// <copyright file="ProviderNetworkException.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Exceptions;

/// <summary>
/// Exception thrown when a network error occurs (connection failure, DNS resolution, etc.).
/// </summary>
public class ProviderNetworkException : ProviderException
{
    public ProviderNetworkException(
        string providerId,
        string message = "Network error - please check your connection",
        Exception? innerException = null)
        : base(providerId, message, ProviderErrorType.NetworkError, innerException: innerException)
    {
    }
}
