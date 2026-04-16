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
        "MiniMax.chat",
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

        // Use only the first model as the primary display; additional models are
        // included as named cards so they can be shown in grouped view.
        for (var i = 0; i < modelRemains.Count; i++)
        {
            var model = modelRemains[i];
            var modelName = model.ModelName ?? $"Model {(i + 1).ToString(CultureInfo.InvariantCulture)}";
            var modelSlug = modelName.ToLowerInvariant().Replace(" ", "-", StringComparison.Ordinal);

            // 5h burst window — note: current_interval_usage_count is REMAINING, not used
            if (model.IntervalTotal > 0)
            {
                var remaining = Math.Max(0, Math.Min(model.IntervalRemaining, model.IntervalTotal));
                var used = model.IntervalTotal - remaining;
                var usedPct = Math.Clamp((used / model.IntervalTotal) * 100.0, 0, 100);
                var resetTime = model.IntervalEndMs > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(model.IntervalEndMs).UtcDateTime
                    : (DateTime?)null;

                usages.Add(new ProviderUsage
                {
                    ProviderId = providerId,
                    ProviderName = providerLabel,
                    CardId = i == 0 ? "burst" : $"{modelSlug}.burst",
                    GroupId = providerId,
                    Name = i == 0 ? "5h" : $"{modelName} 5h",
                    WindowKind = WindowKind.Burst,
                    UsedPercent = usedPct,
                    RequestsUsed = used,
                    RequestsAvailable = model.IntervalTotal,
                    IsQuotaBased = true,
                    PlanType = PlanType.Coding,
                    IsAvailable = true,
                    Description = $"{Math.Clamp(100.0 - usedPct, 0, 100).ToString("F0", CultureInfo.InvariantCulture)}% remaining ({modelName})",
                    NextResetTime = resetTime,
                    PeriodDuration = TimeSpan.FromHours(5),
                    RawJson = rawJson,
                    HttpStatus = httpStatus,
                });
            }

            // Weekly rolling window — same naming convention, current_weekly_usage_count is REMAINING
            if (model.WeeklyTotal > 0)
            {
                var remaining = Math.Max(0, Math.Min(model.WeeklyRemaining, model.WeeklyTotal));
                var used = model.WeeklyTotal - remaining;
                var usedPct = Math.Clamp((used / model.WeeklyTotal) * 100.0, 0, 100);
                var resetTime = model.WeeklyEndMs > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds(model.WeeklyEndMs).UtcDateTime
                    : (DateTime?)null;

                usages.Add(new ProviderUsage
                {
                    ProviderId = providerId,
                    ProviderName = providerLabel,
                    CardId = i == 0 ? "weekly" : $"{modelSlug}.weekly",
                    GroupId = providerId,
                    Name = i == 0 ? "Weekly" : $"{modelName} Weekly",
                    WindowKind = WindowKind.Rolling,
                    UsedPercent = usedPct,
                    RequestsUsed = used,
                    RequestsAvailable = model.WeeklyTotal,
                    IsQuotaBased = true,
                    PlanType = PlanType.Coding,
                    IsAvailable = true,
                    Description = $"{Math.Clamp(100.0 - usedPct, 0, 100).ToString("F0", CultureInfo.InvariantCulture)}% remaining ({modelName})",
                    NextResetTime = resetTime,
                    PeriodDuration = TimeSpan.FromDays(7),
                    RawJson = rawJson,
                    HttpStatus = httpStatus,
                });
            }
        }

        return usages;
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
