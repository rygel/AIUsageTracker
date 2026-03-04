namespace AIUsageTracker.Infrastructure.Constants;

/// <summary>
/// Standard HTTP header names and values used across providers.
/// </summary>
public static class HttpHeaders
{
    /// <summary>
    /// Common HTTP header names
    /// </summary>
    public static class Names
    {
        public const string Authorization = "Authorization";
        public const string Accept = "Accept";
        public const string ContentType = "Content-Type";
        public const string UserAgent = "User-Agent";
        public const string RetryAfter = "Retry-After";
        public const string XRequestId = "X-Request-ID";
        public const string XRateLimitLimit = "X-RateLimit-Limit";
        public const string XRateLimitRemaining = "X-RateLimit-Remaining";
        public const string XRateLimitReset = "X-RateLimit-Reset";
    }

    /// <summary>
    /// Common HTTP header values
    /// </summary>
    public static class Values
    {
        public const string ApplicationJson = "application/json";
        public const string TextPlain = "text/plain";
        public const string BearerPrefix = "Bearer ";
    }

    /// <summary>
    /// Standard User-Agent string
    /// </summary>
    public static class UserAgents
    {
        public const string Default = "AIUsageTracker/1.0";
    }
}
