// <copyright file="AnthropicUsageProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class AnthropicUsageProvider : ProviderBase
{
    private const string BaseUrl = "https://api.anthropic.com";
    private const string UsageEndpoint = BaseUrl + "/v1/organizations/usage_report/messages";
    private const string CostEndpoint = BaseUrl + "/v1/organizations/cost_report";
    private const string AnthropicVersion = "2023-06-01";

    private readonly HttpClient _httpClient;
    private readonly ILogger<AnthropicUsageProvider> _logger;

    public AnthropicUsageProvider(HttpClient httpClient, ILogger<AnthropicUsageProvider> logger)
    {
        this._httpClient = httpClient;
        this._logger = logger;
    }

    public static ProviderDefinition StaticDefinition { get; } = new(
        "anthropic-usage",
        "Anthropic (Admin)",
        PlanType.Usage,
        isQuotaBased: false)
    {
        IsCurrencyUsage = true,
        ExplicitApiKeyPrefixes = new[] { "sk-ant-admin01-" },
        BadgeColorHex = "#D4A27F",
        BadgeInitial = "A",
        ShowInMainWindow = false,
        ShowInSettings = false,
    };

    public override ProviderDefinition Definition => StaticDefinition;

    public override string ProviderId => StaticDefinition.ProviderId;

    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(
        ProviderConfig config,
        Action<ProviderUsage>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        var providerLabel = ProviderMetadataCatalog.GetConfiguredDisplayName(config.ProviderId);

        if (string.IsNullOrEmpty(config.ApiKey))
        {
            return new[]
            {
                this.CreateUnavailableUsage("Admin API Key missing", state: ProviderUsageState.Missing),
            };
        }

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var cards = new List<ProviderUsage>();
        var combinedRaw = string.Empty;

        // ── Fetch daily cost ──────────────────────────────────────────────
        var costResult = await this.FetchJsonAsync<CostReportResponse>(
            CostEndpoint + "?start_date=" + today + "&end_date=" + today,
            config,
            this._httpClient,
            this._logger,
            cancellationToken,
            CustomizeAdminRequest).ConfigureAwait(false);

        if (!costResult.IsSuccess)
        {
            return new[] { costResult.FailureUsage! };
        }

        combinedRaw = costResult.RawContent;

        var totalCost = costResult.Data?.Data?
            .Sum(d => d.CostUsd) ?? 0;

        if (totalCost > 0)
        {
            cards.Add(new WindowedProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = providerLabel,
                Name = "Daily Cost",
                CardId = "daily-cost",
                GroupId = this.ProviderId,
                IsAvailable = true,
                IsCurrencyUsage = true,
                PlanType = this.Definition.PlanType,
                IsQuotaBased = this.Definition.IsQuotaBased,
                UsedPercent = 0,
                Description = string.Format(CultureInfo.InvariantCulture, "${0:F2} today", totalCost),
                RawJson = combinedRaw,
                HttpStatus = costResult.HttpStatus,
            });
        }

        // ── Fetch daily token usage ───────────────────────────────────────
        var usageResult = await this.FetchJsonAsync<UsageReportResponse>(
            UsageEndpoint + "?start_date=" + today + "&end_date=" + today,
            config,
            this._httpClient,
            this._logger,
            cancellationToken,
            CustomizeAdminRequest).ConfigureAwait(false);

        if (usageResult.IsSuccess)
        {
            combinedRaw += "\n---\n" + usageResult.RawContent;

            var totalInputTokens = usageResult.Data?.Data?
                .Sum(d => d.InputTokens) ?? 0;
            var totalOutputTokens = usageResult.Data?.Data?
                .Sum(d => d.OutputTokens) ?? 0;
            var totalTokens = totalInputTokens + totalOutputTokens;

            if (totalTokens > 0)
            {
                cards.Add(new WindowedProviderUsage
                {
                    ProviderId = this.ProviderId,
                    ProviderName = providerLabel,
                    Name = "Daily Tokens",
                    CardId = "daily-tokens",
                    GroupId = this.ProviderId,
                    IsAvailable = true,
                    IsCurrencyUsage = true,
                    PlanType = this.Definition.PlanType,
                    IsQuotaBased = this.Definition.IsQuotaBased,
                    UsedPercent = 0,
                    Description = string.Format(CultureInfo.InvariantCulture, "{0:N0} tokens ({1:N0} in / {2:N0} out)", totalTokens, totalInputTokens, totalOutputTokens),
                    RawJson = combinedRaw,
                    HttpStatus = usageResult.HttpStatus,
                });
            }
        }

        if (cards.Count == 0)
        {
            cards.Add(new StatusProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = providerLabel,
                IsAvailable = true,
                Description = "Connected (no usage data for today)",
                RawJson = combinedRaw,
                HttpStatus = costResult.HttpStatus,
            });
        }

        return cards;
    }

    private static void CustomizeAdminRequest(HttpRequestMessage request)
    {
        var apiKey = request.Headers.Authorization?.Parameter;
        request.Headers.Authorization = null;
        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Add("x-api-key", apiKey);
        }

        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private sealed class CostReportResponse
    {
        [JsonPropertyName("data")]
        public List<CostDataPoint>? Data { get; set; }
    }

    private sealed class CostDataPoint
    {
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("total_cost_usd")]
        public double CostUsd { get; set; }
    }

    private sealed class UsageReportResponse
    {
        [JsonPropertyName("data")]
        public List<UsageDataPoint>? Data { get; set; }
    }

    private sealed class UsageDataPoint
    {
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("input_tokens")]
        public long InputTokens { get; set; }

        [JsonPropertyName("output_tokens")]
        public long OutputTokens { get; set; }
    }
}
