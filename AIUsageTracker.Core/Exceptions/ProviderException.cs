// <copyright file="ProviderException.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Exceptions
{
    using System;

    /// <summary>
    /// Base exception for all provider-related errors.
    /// </summary>
    public class ProviderException : Exception
    {
        public string ProviderId { get; }

        public ProviderErrorType ErrorType { get; }

        public int? HttpStatusCode { get; }

        public ProviderException(
            string providerId,
            string message,
            ProviderErrorType errorType = ProviderErrorType.Unknown,
            int? httpStatusCode = null,
            Exception? innerException = null)
            : base(message, innerException)
        {
            this.ProviderId = providerId ?? throw new ArgumentNullException(nameof(providerId));
            this.ErrorType = errorType;
            this.HttpStatusCode = httpStatusCode;
        }
    }

}
