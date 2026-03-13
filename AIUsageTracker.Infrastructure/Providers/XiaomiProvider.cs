// <copyright file="XiaomiProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class XiaomiProvider : ProviderBase
{
    public static ProviderDefinition StaticDefinition { get; } = new(
        providerId: "xiaomi",
        displayName: "Xiaomi",
        planType: PlanType.Coding,
        isQuotaBased: true,
        defaultConfigType: "quota-based",
        includeInWellKnownProviders: true,
        discoveryEnvironmentVariables: new[] { "XIAOMI_API_KEY", "MIMO_API_KEY" },
        iconAssetName: "xiaomi",
        fallbackBadgeColorHex: "#FFA500",
        fallbackBadgeInitial: "Xi");

    public override ProviderDefinition Definition => StaticDefinition;

    public override string ProviderId => StaticDefinition.ProviderId;

    private readonly HttpClient _httpClient;
    private readonly ILogger<XiaomiProvider> _logger;

    public XiaomiProvider(HttpClient httpClient, ILogger<XiaomiProvider> logger)
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
                new ProviderUsage
            {
                ProviderId = config.ProviderId,
                ProviderName = "Xiaomi",
                IsAvailable = false,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                Description = "API Key missing",
            },
            };
        }

        try
        {
            // Endpoint based on research/best-guess
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.xiaomimimo.com/v1/user/balance");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);

            var response = await this._httpClient.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var data = JsonSerializer.Deserialize<XiaomiResponse>(content);

            if (data == null || data.Data == null)
            {
                throw new Exception("Invalid response from Xiaomi API");
            }

            double balance = data.Data.Balance;

            // Assuming quota is available in response or unlimited
            double quota = data.Data.Quota;

            // If quota is 0, treat as pay-as-you-go balance only
            double percentage = 0;
            var used = quota > 0 ? Math.Max(0, quota - balance) : 0;
            if (quota > 0)
            {
                percentage = UsageMath.CalculateRemainingPercent(used, quota);
            }

            return new[]
            {
                new ProviderUsage
            {
                ProviderId = config.ProviderId,
                ProviderName = "Xiaomi",
                RequestsPercentage = percentage,
                RequestsUsed = used,
                RequestsAvailable = quota > 0 ? quota : balance,
                UsageUnit = "Points",
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                IsAvailable = true,
                Description = quota > 0
                    ? $"{balance} remaining / {quota} total"
                    : $"Balance: {balance}",
                RawJson = content,
                HttpStatus = (int)response.StatusCode,
            },
            };
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to fetch Xiaomi usage");
            return new[]
            {
                new ProviderUsage
            {
                ProviderId = config.ProviderId,
                ProviderName = "Xiaomi",
                IsAvailable = false,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                Description = $"Error: {ex.Message}",
            },
            };
        }
    }

    private class XiaomiResponse
    {
        [JsonPropertyName("data")]
        public XiaomiData? Data { get; set; }

        [JsonPropertyName("code")]
        public int Code { get; set; }
    }

    private class XiaomiData
    {
        [JsonPropertyName("balance")]
        public double Balance { get; set; }

        [JsonPropertyName("quota")]
        public double Quota { get; set; }
    }
}
