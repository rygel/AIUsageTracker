// <copyright file="OpenRouterProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class OpenRouterProvider : ProviderBase
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenRouterProvider> _logger;

    public OpenRouterProvider(HttpClient httpClient, ILogger<OpenRouterProvider> logger)
    {
        this._httpClient = httpClient;
        this._logger = logger;
    }

    public static ProviderDefinition StaticDefinition { get; } = new(
        "openrouter",
        "OpenRouter",
        PlanType.Usage,
        isQuotaBased: true,
        defaultConfigType: "pay-as-you-go")
    {
        IncludeInWellKnownProviders = true,
        DiscoveryEnvironmentVariables = new[] { "OPENROUTER_API_KEY" },
        RooConfigPropertyNames = new[] { "openrouterApiKey" },
        IconAssetName = "openai",
        BadgeColorHex = "#483D8B",
        BadgeInitial = "OR",
    };

    /// <inheritdoc/>
    public override ProviderDefinition Definition => StaticDefinition;

    /// <inheritdoc/>
    public override string ProviderId => StaticDefinition.ProviderId;

    /// <inheritdoc/>
    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        this._logger.LogDebug("Starting OpenRouter usage fetch for provider {ProviderId}", config.ProviderId);

        if (string.IsNullOrEmpty(config.ApiKey))
        {
            this._logger.LogWarning("OpenRouter API key is missing for provider {ProviderId}", config.ProviderId);
            return new[]
            {
                this.CreateUnavailableUsage(
                "API Key missing - please configure OPENROUTER_API_KEY",
                state: ProviderUsageState.Missing),
            };
        }

        // Try to fetch credits first
        OpenRouterCreditsResponse? creditsData = null;
        string? creditsResponseBody = null;
        int httpStatus = 200;

        try
        {
            this._logger.LogDebug("Calling OpenRouter credits API: https://openrouter.ai/api/v1/credits");

            var request = CreateBearerRequest(HttpMethod.Get, "https://openrouter.ai/api/v1/credits", config.ApiKey);

            var response = await this._httpClient.SendAsync(request).ConfigureAwait(false);
            httpStatus = (int)response.StatusCode;
            creditsResponseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            this._logger.LogDebug("OpenRouter credits API response status: {StatusCode}", response.StatusCode);
            this._logger.LogTrace("OpenRouter credits API response body: {ResponseBody}", creditsResponseBody);

            if (!response.IsSuccessStatusCode)
            {
                this._logger.LogError(
                    "OpenRouter credits API failed with status {StatusCode}. Response: {Response}",
                    response.StatusCode,
                    creditsResponseBody);
                return new[] { this.CreateUnavailableUsageFromStatus(response) };
            }

            try
            {
                creditsData = System.Text.Json.JsonSerializer.Deserialize<OpenRouterCreditsResponse>(creditsResponseBody);
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "Failed to deserialize OpenRouter credits response. Raw response: {Response}", creditsResponseBody);
                return new[]
                {
                    this.CreateUnavailableUsage(
                    "Failed to parse credits response - API format may have changed"),
                };
            }

            if (creditsData?.Data == null)
            {
                this._logger.LogError("OpenRouter credits response missing 'data' field. Response: {Response}", creditsResponseBody);
                return new[]
                {
                    this.CreateUnavailableUsage(
                    "Invalid response format - missing data field"),
                };
            }

            this._logger.LogDebug(
                "Successfully parsed credits data - Total: {Total}, Usage: {Usage}",
                creditsData.Data.TotalCredits,
                creditsData.Data.TotalUsage);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Exception while calling OpenRouter credits API");
            return new[]
            {
                this.CreateUnavailableUsageFromException(
                ex,
                context: "Credits API call failed"),
            };
        }

        // Try to fetch additional key info (optional - for limits, labels, etc.)
        var details = new List<ProviderUsageDetail>();
        string label = "OpenRouter";

        try
        {
            this._logger.LogDebug("Calling OpenRouter key API: https://openrouter.ai/api/v1/key");

            var keyRequest = CreateBearerRequest(HttpMethod.Get, "https://openrouter.ai/api/v1/key", config.ApiKey);

            var keyResponse = await this._httpClient.SendAsync(keyRequest).ConfigureAwait(false);
            var keyResponseBody = await keyResponse.Content.ReadAsStringAsync().ConfigureAwait(false);

            this._logger.LogDebug("OpenRouter key API response status: {StatusCode}", keyResponse.StatusCode);
            this._logger.LogTrace("OpenRouter key API response body: {ResponseBody}", keyResponseBody);

            if (keyResponse.IsSuccessStatusCode)
            {
                OpenRouterKeyResponse? keyData = null;

                try
                {
                    keyData = System.Text.Json.JsonSerializer.Deserialize<OpenRouterKeyResponse>(keyResponseBody);
                }
                catch (Exception ex)
                {
                    this._logger.LogWarning(ex, "Failed to deserialize OpenRouter key response. Response: {Response}", keyResponseBody);
                }

                if (keyData?.Data != null)
                {
                    label = keyData.Data.Label ?? "OpenRouter";
                    this._logger.LogDebug(
                        "OpenRouter key label: {Label}, Limit: {Limit}, IsFreeTier: {IsFreeTier}",
                        label,
                        keyData.Data.Limit,
                        keyData.Data.IsFreeTier);

                    if (keyData.Data.Limit > 0)
                    {
                        DateTime? nextResetTime = null;

                        if (!string.IsNullOrEmpty(keyData.Data.LimitReset))
                        {
                            this._logger.LogDebug("Parsing limit reset time: {LimitReset}", keyData.Data.LimitReset);

                            if (DateTime.TryParse(keyData.Data.LimitReset, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt))
                            {
                                var diff = dt.ToLocalTime() - DateTime.Now;
                                if (diff.TotalSeconds > 0)
                                {
                                    nextResetTime = dt.ToLocalTime();
                                    this._logger.LogDebug("Limit reset time parsed successfully: {ResetTime}", nextResetTime);
                                }
                            }
                            else
                            {
                                this._logger.LogWarning("Failed to parse limit reset time: {LimitReset}", keyData.Data.LimitReset);
                            }
                        }

                        details.Add(new ProviderUsageDetail
                        {
                            Name = "Spending Limit",
                            Description = keyData.Data.Limit.ToString("F2", CultureInfo.InvariantCulture),
                            NextResetTime = nextResetTime,
                            DetailType = ProviderUsageDetailType.Other,
                            QuotaBucketKind = WindowKind.None,
                        });
                    }
                    else
                    {
                        this._logger.LogDebug("No spending limit set for this key");
                    }

                    details.Add(new ProviderUsageDetail
                    {
                        Name = "Free Tier",
                        Description = keyData.Data.IsFreeTier ? "Yes" : "No",
                        DetailType = ProviderUsageDetailType.Other,
                        QuotaBucketKind = WindowKind.None,
                    });
                }
                else
                {
                    this._logger.LogWarning("OpenRouter key API response missing 'data' field. Response: {Response}", keyResponseBody);
                }
            }
            else
            {
                this._logger.LogWarning(
                    "OpenRouter key API returned {StatusCode}. Key info unavailable. Response: {Response}",
                    keyResponse.StatusCode,
                    keyResponseBody);
            }
        }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "Exception while calling OpenRouter key API - continuing with credits data only");
        }

        // Calculate usage statistics
        var total = creditsData.Data.TotalCredits;
        var used = creditsData.Data.TotalUsage;
        var remainingPercentage = UsageMath.CalculateRemainingPercent(used, total);
        var remaining = total - used;

        this._logger.LogInformation(
            "OpenRouter usage calculated - Total: {Total}, Used: {Used}, Remaining: {Remaining}, RemainingPercentage: {RemainingPercentage}%",
            total,
            used,
            remaining,
            remainingPercentage);

        // Find spending limit detail for reset time (use typed fields, not string matching)
        string mainReset = string.Empty;
        DateTime? spendingLimitResetTime = null;
        var spendingLimitDetail = details.FirstOrDefault(d => d.DetailType == ProviderUsageDetailType.Other && d.NextResetTime.HasValue);
        if (spendingLimitDetail?.NextResetTime.HasValue == true)
        {
            mainReset = $" (Resets: ({spendingLimitDetail.NextResetTime.Value.ToLocalTime():MMM dd HH:mm}))";
            spendingLimitResetTime = spendingLimitDetail.NextResetTime;
        }

        return new[]
        {
            new ProviderUsage
        {
            ProviderId = config.ProviderId,
            ProviderName = label,
            UsedPercent = 100.0 - remainingPercentage,
            RequestsUsed = used,
            RequestsAvailable = total,
            PlanType = this.Definition.PlanType,
            IsQuotaBased = this.Definition.IsQuotaBased,
            IsAvailable = true,
            Description = $"{remaining.ToString("F2", CultureInfo.InvariantCulture)} Credits Remaining{mainReset}",
            NextResetTime = spendingLimitResetTime,
            Details = details,
            RawJson = creditsResponseBody,
            HttpStatus = httpStatus,
        },
        };
    }

    private class OpenRouterCreditsResponse
    {
        [JsonPropertyName("data")]
        public CreditsData? Data { get; set; }
    }

    private class CreditsData
    {
        [JsonPropertyName("total_credits")]
        public double TotalCredits { get; set; }

        [JsonPropertyName("total_usage")]
        public double TotalUsage { get; set; }
    }

    private class OpenRouterKeyResponse
    {
        [JsonPropertyName("data")]
        public KeyData? Data { get; set; }
    }

    private class KeyData
    {
        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("limit")]
        public double Limit { get; set; }

        [JsonPropertyName("limit_reset")]
        public string? LimitReset { get; set; }

        [JsonPropertyName("is_free_tier")]
        public bool IsFreeTier { get; set; }
    }
}
