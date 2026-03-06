using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Exceptions;

namespace AIUsageTracker.Core.Providers;

public abstract class ProviderBase : IProviderService
{
    /// <summary>
    /// Shared JSON options for all providers. Case-insensitive property matching only.
    /// Do NOT add NumberHandling.AllowReadingFromString here — any API that returns numbers
    /// as strings should handle it explicitly on its model class with [JsonNumberHandling],
    /// so that unexpected format changes produce logged errors rather than silent zeros.
    /// </summary>
    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public abstract string ProviderId { get; }
    public abstract ProviderDefinition Definition { get; }

    public virtual bool CanHandleProviderId(string providerId)
    {
        return Definition.HandlesProviderId(providerId);
    }

    public abstract Task<IEnumerable<ProviderUsage>> GetUsageAsync(
        ProviderConfig config, 
        Action<ProviderUsage>? progressCallback = null);

    protected virtual ProviderUsage CreateUnavailableUsage(
        string description, 
        int httpStatus = 0,
        string? authSource = null,
        PlanType planType = PlanType.Coding,
        bool isQuotaBased = true,
        string? usageUnit = null)
    {
        return new ProviderUsage
        {
            ProviderId = ProviderId,
            ProviderName = Definition.DisplayName ?? ProviderId,
            IsAvailable = false,
            Description = description,
            PlanType = planType,
            IsQuotaBased = isQuotaBased,
            UsageUnit = usageUnit ?? GetDefaultUsageUnit(),
            AuthSource = authSource ?? string.Empty,
            HttpStatus = httpStatus,
            RequestsPercentage = 0,
            RequestsUsed = 0,
            RequestsAvailable = 0
        };
    }

    protected virtual string GetDefaultUsageUnit()
    {
        return "Credits";
    }

    protected virtual ProviderUsage CreateUnavailableUsageFromStatus(
        HttpResponseMessage response, 
        string? authSource = null)
    {
        var statusCode = (int)response.StatusCode;
        
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return CreateUnavailableUsage(
                $"Authentication failed ({statusCode})", 
                statusCode, 
                authSource);
        }
        
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return CreateUnavailableUsage(
                $"Access denied ({statusCode})", 
                statusCode, 
                authSource);
        }
        
        if ((int)response.StatusCode >= 500)
        {
            return CreateUnavailableUsage(
                $"Server error ({statusCode})", 
                statusCode, 
                authSource);
        }
        
        return CreateUnavailableUsage(
            $"Request failed ({statusCode})", 
            statusCode, 
            authSource);
    }

    protected virtual ProviderUsage CreateUnavailableUsageFromException(
        Exception ex, 
        string context = "Provider check failed",
        string? authSource = null)
    {
        var message = ex switch
        {
            HttpRequestException => "Connection failed - check network",
            TaskCanceledException => "Request timed out",
            InvalidOperationException => $"Invalid operation: {ex.Message}",
            _ => $"{context}: {ex.Message}"
        };

        return CreateUnavailableUsage(message, 0, authSource);
    }

    protected virtual ProviderUsage CreateUnavailableUsageFromProviderException(
        ProviderException ex,
        string? authSource = null)
    {
        var description = ex.ErrorType switch
        {
            ProviderErrorType.AuthenticationError => $"Authentication failed ({ex.HttpStatusCode})",
            ProviderErrorType.AuthorizationError => $"Access denied ({ex.HttpStatusCode})",
            ProviderErrorType.NetworkError => "Connection failed - check network",
            ProviderErrorType.TimeoutError => "Request timed out",
            ProviderErrorType.RateLimitError => "Rate limit exceeded - please wait before retrying",
            ProviderErrorType.ServerError => $"Server error ({ex.HttpStatusCode})",
            ProviderErrorType.ConfigurationError => "Configuration error",
            ProviderErrorType.DeserializationError => "Failed to parse response",
            ProviderErrorType.InvalidResponseError => "Invalid response from provider",
            _ => ex.Message
        };

        return CreateUnavailableUsage(
            description,
            ex.HttpStatusCode ?? 0,
            authSource);
    }
}
