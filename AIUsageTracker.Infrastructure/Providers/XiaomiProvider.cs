// <copyright file="XiaomiProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class XiaomiProvider : ProviderBase
{
    private const string UserBalanceEndpoint = "https://api.xiaomimimo.com/v1/user/balance";

    private readonly HttpClient _httpClient;
    private readonly ILogger<XiaomiProvider> _logger;

    public XiaomiProvider(HttpClient httpClient, ILogger<XiaomiProvider> logger)
    {
        this._httpClient = httpClient;
        this._logger = logger;
    }

    public static ProviderDefinition StaticDefinition { get; } = new(
        "xiaomi",
        "Xiaomi",
        PlanType.Coding,
        isQuotaBased: true)
    {
        DiscoveryEnvironmentVariables = new[] { "XIAOMI_API_KEY", "MIMO_API_KEY" },
        IconAssetName = "xiaomi",
        BadgeColorHex = "#FFA500",
        BadgeInitial = "Xi",
    };

    public override ProviderDefinition Definition => StaticDefinition;

    public override string ProviderId => StaticDefinition.ProviderId;

    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var providerLabel = ProviderMetadataCatalog.GetConfiguredDisplayName(config.ProviderId);

        if (string.IsNullOrEmpty(config.ApiKey))
        {
            return new[]
            {
                new ProviderUsage
            {
                ProviderId = config.ProviderId,
                ProviderName = providerLabel,
                IsAvailable = false,
                IsQuotaBased = this.Definition.IsQuotaBased,
                PlanType = this.Definition.PlanType,
                Description = "API Key missing",
                State = ProviderUsageState.Missing,
            },
            };
        }

        try
        {
            // Endpoint based on research/best-guess
            var request = CreateBearerRequest(HttpMethod.Get, UserBalanceEndpoint, config.ApiKey);

            var response = await this._httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var data = DeserializeJsonOrDefault<XiaomiResponse>(content);

            if (data == null || data.Data == null)
            {
                throw new InvalidOperationException("Invalid response from Xiaomi API");
            }

            double balance = data.Data.Balance;

            // Assuming quota is available in response or unlimited
            double quota = data.Data.Quota;

            // If quota is 0, treat as pay-as-you-go balance only
            var used = quota > 0 ? Math.Max(0, quota - balance) : 0;
            var usedPercent = quota > 0 ? UsageMath.CalculateUsedPercent(used, quota) : 0;

            return new[]
            {
                new ProviderUsage
            {
                ProviderId = config.ProviderId,
                ProviderName = providerLabel,
                UsedPercent = usedPercent,
                RequestsUsed = used,
                RequestsAvailable = quota > 0 ? quota : balance,
                IsQuotaBased = this.Definition.IsQuotaBased,
                PlanType = this.Definition.PlanType,
                IsAvailable = true,
                Description = quota > 0
                    ? $"{balance.ToString(CultureInfo.InvariantCulture)} remaining / {quota.ToString(CultureInfo.InvariantCulture)} total"
                    : $"Balance: {balance.ToString(CultureInfo.InvariantCulture)}",
                RawJson = content,
                HttpStatus = (int)response.StatusCode,
            },
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            this._logger.LogError(ex, "Failed to fetch Xiaomi usage");
            return new[]
            {
                new ProviderUsage
            {
                ProviderId = config.ProviderId,
                ProviderName = providerLabel,
                IsAvailable = false,
                IsQuotaBased = this.Definition.IsQuotaBased,
                PlanType = this.Definition.PlanType,
                Description = $"Error: {ex.Message}",
            },
            };
        }
    }

    private sealed class XiaomiResponse
    {
        [JsonPropertyName("data")]
        public XiaomiData? Data { get; set; }

        [JsonPropertyName("code")]
        public int Code { get; set; }
    }

    private sealed class XiaomiData
    {
        [JsonPropertyName("balance")]
        public double Balance { get; set; }

        [JsonPropertyName("quota")]
        public double Quota { get; set; }
    }
}
