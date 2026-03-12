// <copyright file="DeepSeekProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class DeepSeekProvider : ProviderBase
{
    public static ProviderDefinition StaticDefinition { get; } = new(
        providerId: "deepseek",
        displayName: "DeepSeek",
        planType: PlanType.Usage,
        isQuotaBased: false,
        defaultConfigType: "pay-as-you-go",
        includeInWellKnownProviders: true,
        discoveryEnvironmentVariables: new[] { "DEEPSEEK_API_KEY" },
        rooConfigPropertyNames: new[] { "deepseekApiKey" },
        iconAssetName: "deepseek",
        fallbackBadgeColorHex: "#00BFFF",
        fallbackBadgeInitial: "DS");

    public override ProviderDefinition Definition => StaticDefinition;

    public override string ProviderId => StaticDefinition.ProviderId;

    private readonly HttpClient _httpClient;
    private readonly ILogger<DeepSeekProvider> _logger;

    public DeepSeekProvider(HttpClient httpClient, ILogger<DeepSeekProvider> logger)
    {
        this._httpClient = httpClient;
        this._logger = logger;
    }

    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            return new[]
            {
                this.CreateUnavailableUsage(
                "API Key missing",
                planType: PlanType.Usage,
                isQuotaBased: false),
            };
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/user/balance");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var response = await this._httpClient.SendAsync(request).ConfigureAwait(false);
            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                this._logger.LogWarning("DeepSeek API error: {StatusCode} - {ErrorContent}", response.StatusCode, content);

                return new[]
                {
                    new ProviderUsage
                {
                    ProviderId = this.ProviderId,
                    ProviderName = this.Definition.DisplayName ?? this.ProviderId,
                    IsAvailable = true, // Key exists, just failed request
                    Description = $"API Error ({response.StatusCode})",
                    PlanType = PlanType.Usage,
                    IsQuotaBased = false,
                    HttpStatus = (int)response.StatusCode,
                    RequestsPercentage = 0,
                    RequestsUsed = 0,
                    RequestsAvailable = 0,
                    RawJson = content
                },
                };
            }

            var result = JsonSerializer.Deserialize<DeepSeekBalanceResponse>(content);

            if (result == null)
            {
                return new[]
                {
                    this.CreateUnavailableUsage(
                    "Failed to parse DeepSeek response",
                    planType: PlanType.Usage,
                    isQuotaBased: false),
                };
            }

            var details = new List<ProviderUsageDetail>();
            string mainDescription = "No balance found";

            if (result.BalanceInfos != null && result.BalanceInfos.Any())
            {
                foreach (var info in result.BalanceInfos)
                {
                    string currencySymbol = string.Equals(info.Currency, "CNY", StringComparison.OrdinalIgnoreCase) ? "¥" : "$";
                    string detailName = $"Balance ({info.Currency})";
                    details.Add(new ProviderUsageDetail
                    {
                        Name = detailName,
                        Used = $"{currencySymbol}{info.TotalBalance.ToString("F2", CultureInfo.InvariantCulture)}",
                        Description = $"{currencySymbol}{info.ToppedUpBalance.ToString("F2", CultureInfo.InvariantCulture)} (Topped-up: {currencySymbol}{info.ToppedUpBalance.ToString("F2", CultureInfo.InvariantCulture)}, Granted: {currencySymbol}{info.GrantedBalance.ToString("F2", CultureInfo.InvariantCulture)})",
                        DetailType = ProviderUsageDetailType.Credit,
                        QuotaBucketKind = WindowKind.None,
                    });

                    // If it's the first or a primary currency, use for main description
                    if (string.Equals(mainDescription, "No balance found", StringComparison.Ordinal))
                    {
                        mainDescription = $"Balance: {currencySymbol}{info.TotalBalance.ToString("F2", CultureInfo.InvariantCulture)}";
                    }
                }
            }

            return new[]
            {
                new ProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = "DeepSeek",
                IsAvailable = true,
                RequestsPercentage = 0, // Pay-as-you-go/Balance model
                RequestsUsed = 0,
                RequestsAvailable = 0,
                UsageUnit = "Currency",
                IsQuotaBased = false,
                PlanType = PlanType.Usage,
                Description = mainDescription,
                Details = details,
                RawJson = content,
                HttpStatus = (int)response.StatusCode
            },
            };
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "DeepSeek check failed");
            return new[] { this.CreateUnavailableUsageFromException(ex, "DeepSeek check failed") };
        }
    }

    private class DeepSeekBalanceResponse
    {
        [JsonPropertyName("is_available")]
        public bool IsAvailable { get; set; }

        [JsonPropertyName("balance_infos")]
        public List<BalanceInfo>? BalanceInfos { get; set; }
    }

    private class BalanceInfo
    {
        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("total_balance")]
        public double TotalBalance { get; set; }

        [JsonPropertyName("granted_balance")]
        public double GrantedBalance { get; set; }

        [JsonPropertyName("topped_up_balance")]
        public double ToppedUpBalance { get; set; }
    }
}
