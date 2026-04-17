// <copyright file="MinimaxProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using AIUsageTracker.Infrastructure.Constants;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class MinimaxProvider : ProviderBase
{
    public const string ChinaProviderId = "minimax";
    public const string InternationalProviderId = "minimax-io";
    public const string InternationalLegacyProviderId = "minimax-global";
    public const string CodingPlanProviderId = "minimax-coding-plan";

    private readonly HttpClient _httpClient;
    private readonly ILogger<MinimaxProvider> _logger;

    public MinimaxProvider(HttpClient httpClient, ILogger<MinimaxProvider> logger)
    {
        this._httpClient = httpClient;
        this._logger = logger;
    }

    public static ProviderDefinition StaticDefinition { get; } = new(
        ChinaProviderId,
        "MiniMax.com",
        PlanType.Coding,
        isQuotaBased: true)
    {
        AdditionalHandledProviderIds = new[] { InternationalProviderId, InternationalLegacyProviderId, CodingPlanProviderId },
        DisplayNameOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [InternationalProviderId] = "MiniMax.io",
            [InternationalLegacyProviderId] = "MiniMax.io",
            [CodingPlanProviderId] = "Minimax.io Coding Plan",
        },
        SettingsAdditionalProviderIds = new[] { InternationalProviderId, CodingPlanProviderId },
        DiscoveryEnvironmentVariables = new[] { "MINIMAX_API_KEY" },
        IconAssetName = "minimax",
        BadgeColorHex = "#00CED1",
        BadgeInitial = "MM",
    };

    /// <inheritdoc/>
    public override ProviderDefinition Definition => StaticDefinition;

    /// <inheritdoc/>
    public override string ProviderId => StaticDefinition.ProviderId;

    /// <inheritdoc/>
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
                    Description = "API Key not found.",
                    State = ProviderUsageState.Missing,
                },
            };
        }

        if (string.Equals(config.ProviderId, CodingPlanProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return await this.GetCodingPlanUsageAsync(config, providerLabel, cancellationToken).ConfigureAwait(false);
        }

        return await this.GetTokenUsageAsync(config, providerLabel, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IEnumerable<ProviderUsage>> GetTokenUsageAsync(
        ProviderConfig config,
        string providerLabel,
        CancellationToken cancellationToken)
    {
        string url;
        if (!string.IsNullOrEmpty(config.BaseUrl))
        {
            url = config.BaseUrl;
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }
        }
        else
        {
            url = string.Equals(config.ProviderId, InternationalProviderId, StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(config.ProviderId, InternationalLegacyProviderId, StringComparison.OrdinalIgnoreCase)
                ? ProviderEndpoints.Minimax.UserUsage
                : ProviderEndpoints.Minimax.ChatUserUsage;
        }

        var request = CreateBearerRequest(HttpMethod.Get, url, config.ApiKey);
        var response = await this._httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var httpStatus = (int)response.StatusCode;

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = config.ProviderId,
                    ProviderName = providerLabel,
                    IsAvailable = false,
                    IsQuotaBased = this.Definition.IsQuotaBased,
                    PlanType = this.Definition.PlanType,
                    Description = $"API returned {response.StatusCode} for {url}",
                    RawJson = errorContent,
                    HttpStatus = httpStatus,
                },
            };
        }

        var responseString = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var minimax = JsonSerializer.Deserialize<MinimaxTokenResponse>(responseString);
            if (minimax?.Usage == null)
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
                        Description = "Invalid Minimax response format",
                        RawJson = responseString,
                        HttpStatus = httpStatus,
                    },
                };
            }

            var used = minimax.Usage.TokensUsed;
            var total = minimax.Usage.TokensLimit > 0 ? minimax.Usage.TokensLimit : 0;
            var utilization = total > 0 ? (used / total) * 100.0 : 0;

            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = config.ProviderId,
                    ProviderName = providerLabel,
                    UsedPercent = Math.Clamp(utilization, 0, 100),
                    RequestsUsed = used,
                    RequestsAvailable = total,
                    PlanType = this.Definition.PlanType,
                    IsQuotaBased = this.Definition.IsQuotaBased,
                    Description = $"{used.ToString("N0", CultureInfo.InvariantCulture)} tokens used" + (total > 0 ? $" / {total.ToString("N0", CultureInfo.InvariantCulture)} limit" : string.Empty),
                    RawJson = responseString,
                    HttpStatus = httpStatus,
                },
            };
        }
        catch (JsonException ex)
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
                    Description = $"Failed to parse Minimax response: {ex.Message}",
                    RawJson = responseString,
                    HttpStatus = httpStatus,
                },
            };
        }
    }

    private async Task<IEnumerable<ProviderUsage>> GetCodingPlanUsageAsync(
        ProviderConfig config,
        string providerLabel,
        CancellationToken cancellationToken)
    {
        string url;
        if (!string.IsNullOrEmpty(config.BaseUrl))
        {
            url = config.BaseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? config.BaseUrl
                : "https://" + config.BaseUrl;
        }
        else
        {
            url = ProviderEndpoints.Minimax.CodingPlanRemains;
        }

        var request = CreateBearerRequest(HttpMethod.Get, url, config.ApiKey);
        var response = await this._httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var httpStatus = (int)response.StatusCode;
        var responseString = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
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
                    Description = $"API returned {response.StatusCode}",
                    RawJson = responseString,
                    HttpStatus = httpStatus,
                },
            };
        }

        try
        {
            var codingPlan = JsonSerializer.Deserialize<MinimaxCodingPlanResponse>(responseString);
            if (codingPlan?.BaseResp?.StatusCode != 0 || codingPlan.ModelRemains == null || codingPlan.ModelRemains.Count == 0)
            {
                var errMsg = codingPlan?.BaseResp?.StatusMsg ?? "Invalid MiniMax Coding Plan response";
                return new[]
                {
                    new ProviderUsage
                    {
                        ProviderId = config.ProviderId,
                        ProviderName = providerLabel,
                        IsAvailable = false,
                        IsQuotaBased = this.Definition.IsQuotaBased,
                        PlanType = this.Definition.PlanType,
                        Description = errMsg,
                        RawJson = responseString,
                        HttpStatus = httpStatus,
                    },
                };
            }

            return BuildCodingPlanUsages(config.ProviderId, providerLabel, codingPlan.ModelRemains, responseString, httpStatus);
        }
        catch (JsonException ex)
        {
            this._logger.LogWarning(ex, "Failed to parse MiniMax Coding Plan response");
            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = config.ProviderId,
                    ProviderName = providerLabel,
                    IsAvailable = false,
                    IsQuotaBased = this.Definition.IsQuotaBased,
                    PlanType = this.Definition.PlanType,
                    Description = $"Failed to parse MiniMax Coding Plan response: {ex.Message}",
                    RawJson = responseString,
                    HttpStatus = httpStatus,
                },
            };
        }
    }

    private static IEnumerable<ProviderUsage> BuildCodingPlanUsages(
        string providerId,
        string providerLabel,
        IReadOnlyList<MinimaxModelRemains> modelRemains,
        string rawJson,
        int httpStatus)
    {
        var usages = new List<ProviderUsage>();

        for (var i = 0; i < modelRemains.Count; i++)
        {
            var model = modelRemains[i];
            var modelName = model.ModelName ?? $"Model {(i + 1).ToString(CultureInfo.InvariantCulture)}";
            var modelSlug = modelName.ToLowerInvariant().Replace(" ", "-", StringComparison.Ordinal);

            if (model.IntervalTotal > 0)
            {
                usages.Add(BuildModelWindowCard(providerId, providerLabel, modelName, modelSlug, i,
                    model.IntervalTotal, model.IntervalRemaining, model.IntervalEndMs,
                    "burst", "5h", WindowKind.Burst, TimeSpan.FromHours(5), rawJson, httpStatus));
            }

            if (model.WeeklyTotal > 0)
            {
                usages.Add(BuildModelWindowCard(providerId, providerLabel, modelName, modelSlug, i,
                    model.WeeklyTotal, model.WeeklyRemaining, model.WeeklyEndMs,
                    "weekly", "Weekly", WindowKind.Rolling, TimeSpan.FromDays(7), rawJson, httpStatus));
            }
        }

        return usages;
    }

    private static ProviderUsage BuildModelWindowCard(
        string providerId, string providerLabel, string modelName, string modelSlug, int modelIndex,
        double windowTotal, double windowRemaining, long resetMs,
        string cardSuffix, string nameSuffix,
        WindowKind windowKind, TimeSpan periodDuration,
        string rawJson, int httpStatus)
    {
        var remaining = Math.Max(0, Math.Min(windowRemaining, windowTotal));
        var used = windowTotal - remaining;
        var usedPct = Math.Clamp((used / windowTotal) * 100.0, 0, 100);
        var resetTime = resetMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(resetMs).UtcDateTime
            : (DateTime?)null;

        return new ProviderUsage
        {
            ProviderId = providerId,
            ProviderName = providerLabel,
            CardId = modelIndex == 0 ? cardSuffix : $"{modelSlug}.{cardSuffix}",
            GroupId = providerId,
            Name = modelIndex == 0 ? nameSuffix : $"{modelName} {nameSuffix}",
            WindowKind = windowKind,
            UsedPercent = usedPct,
            RequestsUsed = used,
            RequestsAvailable = windowTotal,
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            IsAvailable = true,
            Description = $"{Math.Clamp(100.0 - usedPct, 0, 100).ToString("F0", CultureInfo.InvariantCulture)}% remaining ({modelName})",
            NextResetTime = resetTime,
            PeriodDuration = periodDuration,
            RawJson = rawJson,
            HttpStatus = httpStatus,
        };
    }

    private sealed class MinimaxTokenResponse
    {
        [JsonPropertyName("usage")]
        public MinimaxTokenUsage? Usage { get; set; }
    }

    private sealed class MinimaxTokenUsage
    {
        [JsonPropertyName("tokens_used")]
        public double TokensUsed { get; set; }

        [JsonPropertyName("tokens_limit")]
        public double TokensLimit { get; set; }
    }

    private sealed class MinimaxCodingPlanResponse
    {
        [JsonPropertyName("base_resp")]
        public MinimaxBaseResp? BaseResp { get; set; }

        [JsonPropertyName("model_remains")]
        public List<MinimaxModelRemains>? ModelRemains { get; set; }
    }

    private sealed class MinimaxBaseResp
    {
        [JsonPropertyName("status_code")]
        public int StatusCode { get; set; }

        [JsonPropertyName("status_msg")]
        public string? StatusMsg { get; set; }
    }

    private sealed class MinimaxModelRemains
    {
        [JsonPropertyName("model_name")]
        public string? ModelName { get; set; }

        [JsonPropertyName("current_interval_total_count")]
        public double IntervalTotal { get; set; }

        /// <summary>Remaining count for the 5h window (misleadingly named "usage" in the API).</summary>
        [JsonPropertyName("current_interval_usage_count")]
        public double IntervalRemaining { get; set; }

        [JsonPropertyName("end_time")]
        public long IntervalEndMs { get; set; }

        [JsonPropertyName("current_weekly_total_count")]
        public double WeeklyTotal { get; set; }

        /// <summary>Remaining count for the weekly window (misleadingly named "usage" in the API).</summary>
        [JsonPropertyName("current_weekly_usage_count")]
        public double WeeklyRemaining { get; set; }

        [JsonPropertyName("weekly_end_time")]
        public long WeeklyEndMs { get; set; }
    }
}
