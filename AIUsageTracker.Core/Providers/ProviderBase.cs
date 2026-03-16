// <copyright file="ProviderBase.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageTracker.Core.Exceptions;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;

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
        PropertyNameCaseInsensitive = true,
    };

    protected ProviderBase(IProviderDiscoveryService? discoveryService = null)
    {
        this.DiscoveryService = discoveryService;
    }

    public abstract string ProviderId { get; }

    public abstract ProviderDefinition Definition { get; }

    protected IProviderDiscoveryService? DiscoveryService { get; }

    public virtual bool CanHandleProviderId(string providerId)
    {
        return this.Definition.HandlesProviderId(providerId);
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
        ProviderUsageState state = ProviderUsageState.Error)
    {
        return new ProviderUsage
        {
            ProviderId = this.ProviderId,
            ProviderName = this.Definition.DisplayName ?? this.ProviderId,
            IsAvailable = false,
            Description = description,
            State = state,
            PlanType = planType,
            IsQuotaBased = isQuotaBased,
            AuthSource = authSource ?? string.Empty,
            HttpStatus = httpStatus,
            UsedPercent = 0,
            RequestsUsed = 0,
            RequestsAvailable = 0,
        };
    }

    protected virtual ProviderUsage CreateUnavailableUsageFromStatus(
        HttpResponseMessage response,
        string? authSource = null)
    {
        var statusCode = (int)response.StatusCode;

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return this.CreateUnavailableUsage(
                $"Authentication failed ({statusCode})",
                statusCode,
                authSource);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return this.CreateUnavailableUsage(
                $"Access denied ({statusCode})",
                statusCode,
                authSource);
        }

        if ((int)response.StatusCode >= 500)
        {
            return this.CreateUnavailableUsage(
                $"Server error ({statusCode})",
                statusCode,
                authSource);
        }

        return this.CreateUnavailableUsage(
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
            _ => $"{context}: {ex.Message}",
        };

        return this.CreateUnavailableUsage(message, 0, authSource);
    }

    /// <summary>
    /// Formats a window reset description from a seconds-until-reset value.
    /// Returns an empty string when no reset time is available.
    /// </summary>
    protected static string FormatResetDescription(double? resetAfterSeconds)
    {
        if (!resetAfterSeconds.HasValue || resetAfterSeconds.Value <= 0)
        {
            return string.Empty;
        }

        return $"Resets in {(int)resetAfterSeconds.Value}s";
    }

    /// <summary>
    /// Converts a seconds-until-reset value to an absolute local <see cref="DateTime"/>.
    /// Returns <see langword="null"/> when no reset time is available.
    /// </summary>
    protected static DateTime? ResolveResetTimeFromSeconds(double? resetAfterSeconds)
    {
        if (!resetAfterSeconds.HasValue || resetAfterSeconds.Value <= 0)
        {
            return null;
        }

        return DateTime.UtcNow.AddSeconds(resetAfterSeconds.Value).ToLocalTime();
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
            _ => ex.Message,
        };

        return this.CreateUnavailableUsage(
            description,
            ex.HttpStatusCode ?? 0,
            authSource);
    }
}
