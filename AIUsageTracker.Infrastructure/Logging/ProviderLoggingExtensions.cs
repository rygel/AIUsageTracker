using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Logging;

public static partial class ProviderLoggingExtensions
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "[{Provider}] Sending API request to {Url}")]
    public static partial void LogApiRequest(this ILogger logger, string provider, string url);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{Provider}] API response: {StatusCode}")]
    public static partial void LogApiResponse(this ILogger logger, string provider, int statusCode);

    [LoggerMessage(Level = LogLevel.Error, Message = "[{Provider}] API returned {StatusCode}: {Message}")]
    public static partial void LogApiError(this ILogger logger, string provider, int statusCode, string message);

    [LoggerMessage(Level = LogLevel.Error, Message = "[{Provider}] Request failed: {Message}")]
    public static partial void LogRequestFailed(this ILogger logger, string provider, string message, Exception? ex = null);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[{Provider}] API key not configured")]
    public static partial void LogApiKeyMissing(this ILogger logger, string provider);

    [LoggerMessage(Level = LogLevel.Information, Message = "[{Provider}] Usage retrieved: {Used}/{Total} {Unit}")]
    public static partial void LogUsageRetrieved(this ILogger logger, string provider, double used, double total, string unit);

    [LoggerMessage(Level = LogLevel.Debug, Message = "[{Provider}] Parsing response: {Detail}")]
    public static partial void LogParsingDetail(this ILogger logger, string provider, string detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "[{Provider}] Failed to parse response: {Detail}")]
    public static partial void LogParseWarning(this ILogger logger, string provider, string detail, Exception? ex = null);

    [LoggerMessage(Level = LogLevel.Error, Message = "[{Provider}] Exception: {Message}")]
    public static partial void LogProviderException(this ILogger logger, string provider, string message, Exception? ex = null);
}
