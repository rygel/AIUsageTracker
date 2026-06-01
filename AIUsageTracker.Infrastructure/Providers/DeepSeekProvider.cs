// <copyright file="DeepSeekProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using AIUsageTracker.Infrastructure.Mappers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class DeepSeekProvider : ProviderBase
{
    private const string UserBalanceEndpoint = "https://api.deepseek.com/user/balance";

    private readonly HttpClient _httpClient;
    private readonly ILogger<DeepSeekProvider> _logger;

    public DeepSeekProvider(HttpClient httpClient, ILogger<DeepSeekProvider> logger)
    {
        this._httpClient = httpClient;
        this._logger = logger;
    }

    public static ProviderDefinition StaticDefinition { get; } = new(
        "deepseek",
        "DeepSeek",
        PlanType.Usage,
        isQuotaBased: false)
    {
        ShowInSettings = false,
        DiscoveryEnvironmentVariables = new[] { "DEEPSEEK_API_KEY" },
        RooConfigPropertyNames = new[] { "deepseekApiKey" },
        IsCurrencyUsage = true,
        IconAssetName = "deepseek",
        BadgeColorHex = "#00BFFF",
        BadgeInitial = "DS",
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
                this.CreateUnavailableUsage(
                "API Key missing",
                state: ProviderUsageState.Missing),
            };
        }

        var providerLabel = ProviderMetadataCatalog.GetConfiguredDisplayName(config.ProviderId);

        var fetchResult = await this.FetchJsonAsync<DeepSeekBalanceResponse>(
            UserBalanceEndpoint,
            config,
            this._httpClient,
            this._logger,
            cancellationToken,
            request => request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json")))
            .ConfigureAwait(false);

        if (!fetchResult.IsSuccess)
        {
            var errorUsage = fetchResult.FailureUsage!;
            // DeepSeek: HTTP error means key exists but request failed — show as available with error
            // Network/timeout exceptions should remain unavailable
            if (errorUsage.HttpStatus > 0)
            {
                errorUsage.IsAvailable = true;
                errorUsage.FailureContext = HttpFailureContext.FromHttpStatus(fetchResult.HttpStatus);
            }

            return new[] { errorUsage };
        }

        var result = fetchResult.Data!;
        var content = fetchResult.RawContent;
        var httpStatus = fetchResult.HttpStatus;

        if (result.BalanceInfos == null || result.BalanceInfos.Count == 0)
        {
            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = this.ProviderId,
                    ProviderName = providerLabel,
                    IsAvailable = true,
                    UsedPercent = 0,
                    RequestsUsed = 0,
                    RequestsAvailable = 0,
                    IsQuotaBased = this.Definition.IsQuotaBased,
                    PlanType = this.Definition.PlanType,
                    Description = "No balance found",
                    RawJson = content,
                    HttpStatus = httpStatus,
                },
            };
        }

        var flatCards = new List<ProviderUsage>();
        foreach (var info in result.BalanceInfos)
        {
            var currencyCode = info.Currency ?? "USD";
            var currencySymbol = string.Equals(currencyCode, "CNY", StringComparison.OrdinalIgnoreCase) ? "¥" : "$";
            flatCards.Add(new ProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = providerLabel,
                Name = $"Balance ({currencyCode})",
                CardId = $"balance-{currencyCode.ToLowerInvariant()}",
                GroupId = this.ProviderId,
                Description = string.Format(CultureInfo.InvariantCulture, "{0}{1:F2} ({2:F2} topped-up + {3:F2} granted)", currencySymbol, info.TotalBalance, info.ToppedUpBalance, info.GrantedBalance),
                IsAvailable = true,
                PlanType = this.Definition.PlanType,
                IsCurrencyUsage = true,
                IsQuotaBased = this.Definition.IsQuotaBased,
                UsedPercent = 0,
                RawJson = content,
                HttpStatus = httpStatus,
            });
        }

        return flatCards;
    }

    private sealed class DeepSeekBalanceResponse
    {
        [JsonPropertyName("is_available")]
        public bool IsAvailable { get; set; }

        [JsonPropertyName("balance_infos")]
        public List<BalanceInfo>? BalanceInfos { get; set; }
    }

    private sealed class BalanceInfo
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
