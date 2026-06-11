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

        if (string.IsNullOrEmpty(config.ApiKey))
        {
            return new[]
            {
                this.CreateUnavailableUsage("API Key missing", state: ProviderUsageState.Missing),
            };
        }

        var providerLabel = ProviderMetadataCatalog.GetConfiguredDisplayName(config.ProviderId);

        var fetchResult = await this.FetchJsonAsync<XiaomiResponse>(
            UserBalanceEndpoint,
            config,
            this._httpClient,
            this._logger,
            cancellationToken)
            .ConfigureAwait(false);

        if (!fetchResult.IsSuccess)
        {
            return new[] { fetchResult.FailureUsage! };
        }

        var data = fetchResult.Data!;
        var content = fetchResult.RawContent;
        var httpStatus = fetchResult.HttpStatus;

        if (data.Data == null)
        {
            return new[] { this.CreateUnavailableUsage("Invalid response from Xiaomi API", httpStatus) };
        }

        double balance = data.Data.Balance;
        double quota = data.Data.Quota;

        var used = quota > 0 ? Math.Max(0, quota - balance) : 0;
        var usedPercent = quota > 0 ? UsageMath.CalculateUsedPercent(used, quota) : 0;

        var usage = this.CreateBaseUsage(providerLabel, content, httpStatus);
        usage.UsedPercent = usedPercent;
        usage.RequestsUsed = used;
        usage.RequestsAvailable = quota > 0 ? quota : balance;
        usage.Description = quota > 0
            ? $"{balance.ToString(CultureInfo.InvariantCulture)} remaining / {quota.ToString(CultureInfo.InvariantCulture)} total"
            : $"Balance: {balance.ToString(CultureInfo.InvariantCulture)}";
        return new[] { usage };
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
