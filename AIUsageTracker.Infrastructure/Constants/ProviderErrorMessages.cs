// <copyright file="ProviderErrorMessages.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Infrastructure.Constants
{
    /// <summary>
    /// Standard error messages used across providers.
    /// Using constants ensures consistent messaging and easier localization in the future.
    /// </summary>
    public static class ProviderErrorMessages
    {
        /// <summary>
        /// Authentication and authorization errors
        /// </summary>
        public static class Auth
        {
            public const string ApiKeyMissing = "API Key missing";
            public const string AuthenticationFailed = "Authentication failed";
            public const string AccessDenied = "Access denied - check API key permissions";
            public const string InvalidApiKey = "Invalid API Key";
            public const string TokenExpired = "API token has expired";
        }

        /// <summary>
        /// Network and connection errors
        /// </summary>
        public static class Network
        {
            public const string ConnectionFailed = "Connection failed - check network";
            public const string RequestTimeout = "Request timed out";
            public const string DnsResolutionFailed = "Could not resolve provider hostname";
            public const string SslError = "SSL/TLS connection error";
        }

        /// <summary>
        /// Rate limiting errors
        /// </summary>
        public static class RateLimit
        {
            public const string RateLimitExceeded = "Rate limit exceeded - please wait before retrying";
            public const string QuotaExceeded = "API quota exceeded";
            public const string TooManyRequests = "Too many requests";
        }

        /// <summary>
        /// Server-side errors
        /// </summary>
        public static class Server
        {
            public const string ServerError = "Server error";
            public const string ServiceUnavailable = "Service temporarily unavailable";
            public const string InternalServerError = "Internal server error";
            public const string GatewayTimeout = "Gateway timeout";
        }

        /// <summary>
        /// Data and parsing errors
        /// </summary>
        public static class Data
        {
            public const string ResponseParseError = "Failed to parse provider response";
            public const string InvalidResponseFormat = "Invalid response format";
            public const string MissingData = "Required data missing from response";
            public const string JsonDeserializationFailed = "Failed to deserialize JSON response";
        }

        /// <summary>
        /// Configuration errors
        /// </summary>
        public static class Configuration
        {
            public const string InvalidConfiguration = "Invalid provider configuration";
            public const string EndpointNotConfigured = "API endpoint not configured";
            public const string RequiredFieldMissing = "Required configuration field missing";
        }

        /// <summary>
        /// Generic fallback messages
        /// </summary>
        public static class General
        {
            public const string ProviderCheckFailed = "Provider check failed";
            public const string UnknownError = "Unknown error occurred";
            public const string RequestFailed = "Request failed";
            public const string CheckProviderStatus = "Check failed";
        }
    }
}
