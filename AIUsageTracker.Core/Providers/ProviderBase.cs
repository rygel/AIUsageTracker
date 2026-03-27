// <copyright file="ProviderBase.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net.Http.Headers;
using System.Text.Json;
using AIUsageTracker.Core.Exceptions;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Core.Providers;

public abstract class ProviderBase : IProviderService
{
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

    public bool CanHandleProviderId(string providerId) => this.Definition.HandlesProviderId(providerId);

    public abstract Task<IEnumerable<ProviderUsage>> GetUsageAsync(
        ProviderConfig config,
        Action<ProviderUsage>? progressCallback = null,
        CancellationToken cancellationToken = default);

    protected static string FormatResetDescription(double? resetAfterSeconds)
    {
        if (!resetAfterSeconds.HasValue || resetAfterSeconds.Value <= 0)
        {
            return string.Empty;
        }

        return $"Resets in {(int)resetAfterSeconds.Value}s";
    }

    protected static DateTime? ResolveResetTimeFromSeconds(double? resetAfterSeconds)
    {
        if (!resetAfterSeconds.HasValue || resetAfterSeconds.Value <= 0)
        {
            return null;
        }

        return DateTime.UtcNow.AddSeconds(resetAfterSeconds.Value).ToLocalTime();
    }

    protected static HttpRequestMessage CreateBearerRequest(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    protected static T? DeserializeJsonOrDefault<T>(string content)
        where T : class
    {
        return JsonSerializer.Deserialize<T>(content, JsonOptions);
    }

    protected ProviderUsage CreateUnavailableUsage(
        string description,
        int httpStatus = 0,
        string? authSource = null,
        ProviderUsageState state = ProviderUsageState.Error)
    {
        return new ProviderUsage
        {
            ProviderId = this.ProviderId,
            ProviderName = this.Definition.DisplayName ?? this.ProviderId,
            IsAvailable = false,
            Description = description,
            State = state,
            PlanType = this.Definition.PlanType,
            IsQuotaBased = this.Definition.IsQuotaBased,
            AuthSource = authSource ?? string.Empty,
            HttpStatus = httpStatus,
            UsedPercent = 0,
            RequestsUsed = 0,
            RequestsAvailable = 0,
        };
    }

    protected ProviderUsage CreateUnavailableUsageFromStatus(
        HttpResponseMessage response,
        string? authSource = null)
    {
        var statusCode = (int)response.StatusCode;
        var description = response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => $"Authentication failed ({statusCode})",
            System.Net.HttpStatusCode.Forbidden => $"Access denied ({statusCode})",
            _ when statusCode >= 500 => $"Server error ({statusCode})",
            _ => $"Request failed ({statusCode})",
        };

        return this.CreateUnavailableUsage(description, statusCode, authSource);
    }

    protected ProviderUsage CreateUnavailableUsageFromException(
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

    protected ProviderUsage CreateUnavailableUsageFromProviderException(
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

        return this.CreateUnavailableUsage(description, ex.HttpStatusCode ?? 0, authSource);
    }
}
