// <copyright file="OpenCodeProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class OpenCodeProvider : ProviderBase
{
    public static ProviderDefinition StaticDefinition { get; } = new(
        providerId: "opencode",
        displayName: "OpenCode",
        planType: PlanType.Usage,
        isQuotaBased: false,
        defaultConfigType: "pay-as-you-go",
        includeInWellKnownProviders: true);

    /// <inheritdoc/>
    public override ProviderDefinition Definition => StaticDefinition;

    /// <inheritdoc/>
    public override string ProviderId => StaticDefinition.ProviderId;

    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenCodeProvider> _logger;

    public OpenCodeProvider(HttpClient httpClient, ILogger<OpenCodeProvider> logger)
    {
        this._httpClient = httpClient;
        this._logger = logger;
    }

    /// <inheritdoc/>
    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        this._logger.LogDebug("OpenCode GetUsageAsync called for provider {ProviderId}", this.ProviderId);
        var displayName = this.Definition.DisplayName;

        if (string.IsNullOrEmpty(config.ApiKey))
        {
            this._logger.LogInformation("OpenCode API key not configured - returning unavailable state");
            return new[]
            {
                new ProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = displayName,
                IsAvailable = false,
                Description = "No API key configured",
                IsQuotaBased = false,
                PlanType = PlanType.Usage,
            },
            };
        }

        try
        {
            var url = "https://api.opencode.ai/v1/credits";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);

            var response = await this._httpClient.SendAsync(request).ConfigureAwait(false);
            var httpStatus = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                this._logger.LogWarning("OpenCode API failed with status {StatusCode}", response.StatusCode);
                var errorContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return new[]
                {
                    new ProviderUsage
                {
                    ProviderId = this.ProviderId,
                    ProviderName = displayName,
                    IsAvailable = false,
                    Description = $"API Error: {response.StatusCode}",
                    IsQuotaBased = false,
                    PlanType = PlanType.Usage,
                    RawJson = errorContent,
                    HttpStatus = httpStatus,
                },
                };
            }

            var responseString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var result = this.ParseJsonResponse(responseString, httpStatus);
            this._logger.LogInformation("OpenCode usage retrieved successfully - Total Cost: ${TotalCost:F2}", result.RequestsUsed);

            return new[] { result };
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to call OpenCode API. Message: {Message}", ex.Message);
            return new[]
            {
                new ProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = displayName,
                IsAvailable = false,
                Description = $"API Call Failed: {ex.Message}",
                IsQuotaBased = false,
                PlanType = PlanType.Usage,
            },
            };
        }
    }

    private ProviderUsage ParseJsonResponse(string json, int httpStatus = 200)
    {
        var displayName = this.Definition.DisplayName;
        try
        {
            // Check if response is empty or not JSON
            if (string.IsNullOrWhiteSpace(json))
            {
                this._logger.LogWarning("OpenCode API returned empty response");
                return new ProviderUsage
                {
                    ProviderId = this.ProviderId,
                    ProviderName = displayName,
                    IsAvailable = false,
                    Description = "Empty API response",
                    IsQuotaBased = false,
                    PlanType = PlanType.Usage,
                    RawJson = json,
                    HttpStatus = httpStatus,
                };
            }

            // Log the raw response for debugging
            this._logger.LogDebug("OpenCode raw response: {Response}", json.Length > 200 ? json.Substring(0, 200) + "..." : json);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var response = JsonSerializer.Deserialize<CreditsResponse>(json, options);

            double totalCost = 0;
            if (response?.Data != null)
            {
                totalCost = response.Data.UsedCredits;
            }

            return new ProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = displayName,
                RequestsPercentage = 0,
                RequestsUsed = totalCost,
                UsageUnit = "USD",
                IsQuotaBased = false,
                PlanType = PlanType.Usage,
                IsAvailable = true,
                Description = $"${totalCost.ToString("F2", CultureInfo.InvariantCulture)} used (7 days)",
                RawJson = json,
                HttpStatus = httpStatus,
            };
        }
        catch (JsonException ex)
        {
            this._logger.LogError(ex, "Failed to parse OpenCode JSON response. Response started with: {Start}",
                json.Length > 50 ? json.Substring(0, 50) : json);
            return new ProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = displayName,
                IsAvailable = false,
                Description = "Invalid API response (not JSON)",
                IsQuotaBased = false,
                PlanType = PlanType.Usage,
            };
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to parse OpenCode response");
            return new ProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = displayName,
                IsAvailable = false,
                Description = "Parse Error",
                IsQuotaBased = false,
                PlanType = PlanType.Usage,
            };
        }
    }

    private class CreditsResponse
    {
        [JsonPropertyName("data")]
        public CreditsData? Data { get; set; }
    }

    private class CreditsData
    {
        [JsonPropertyName("total_credits")]
        public double TotalCredits { get; set; }

        [JsonPropertyName("used_credits")]
        public double UsedCredits { get; set; }
    }
}
