using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Core.Providers;

public abstract class ProviderBase : IProviderService
{
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
}
