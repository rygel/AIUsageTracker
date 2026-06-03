// <copyright file="MinimaxProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
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
    private const string TextGenerationLabel = "text generation";
    private const string TextGenerationModelPrefix = "minimax-text";
    private const string CodingModelPrefix = "minimax-m";
    private const string GeneralModelName = "general";

    private readonly HttpClient _httpClient;
    private readonly ILogger<MinimaxProvider> _logger;

    public MinimaxProvider(HttpClient httpClient, ILogger<MinimaxProvider> logger)
    {
        this._httpClient = httpClient;
        this._logger = logger;
    }

    public static ProviderDefinition StaticDefinition { get; } = new(
        ChinaProviderId,
        "MiniMax.io",
        PlanType.Coding,
        isQuotaBased: true)
    {
        QuotaWindows = new QuotaWindowDefinition[]
        {
            new(WindowKind.Burst, "5h", PeriodDuration: TimeSpan.FromHours(5)),
            new(WindowKind.Rolling, "Weekly", PeriodDuration: TimeSpan.FromDays(7)),
        },
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
            url = config.BaseUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? config.BaseUrl
                : "https://" + config.BaseUrl;
        }
        else
        {
            url = ProviderEndpoints.Minimax.TokenPlanRemains;
        }

        return await this.GetRemainsUsageAsync(config, providerLabel, url, cancellationToken).ConfigureAwait(false);
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

        return await this.GetRemainsUsageAsync(config, providerLabel, url, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IEnumerable<ProviderUsage>> GetRemainsUsageAsync(
        ProviderConfig config,
        string providerLabel,
        string url,
        CancellationToken cancellationToken)
    {
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
            var remainsResponse = JsonSerializer.Deserialize<MinimaxRemainsResponse>(responseString);
            if (remainsResponse?.BaseResp?.StatusCode != 0 || remainsResponse.ModelRemains == null || remainsResponse.ModelRemains.Count == 0)
            {
                var errMsg = remainsResponse?.BaseResp is { StatusCode: not 0, StatusMsg: not null }
                    ? remainsResponse.BaseResp.StatusMsg
                    : "Invalid MiniMax response";
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

            var textGenerationModel = FindTextGenerationModel(remainsResponse.ModelRemains);
            if (textGenerationModel == null)
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
                        Description = "MiniMax Text Generation model missing in response",
                        RawJson = responseString,
                        HttpStatus = httpStatus,
                    },
                };
            }

            return BuildRemainsUsages(config.ProviderId, providerLabel, textGenerationModel, responseString, httpStatus);
        }
        catch (JsonException ex)
        {
            this._logger.LogWarning(ex, "Failed to parse MiniMax remains response");
            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = config.ProviderId,
                    ProviderName = providerLabel,
                    IsAvailable = false,
                    IsQuotaBased = this.Definition.IsQuotaBased,
                    PlanType = this.Definition.PlanType,
                    Description = $"Failed to parse MiniMax response: {ex.Message}",
                    RawJson = responseString,
                    HttpStatus = httpStatus,
                },
            };
        }
    }

    private static List<ProviderUsage> BuildRemainsUsages(
        string providerId,
        string providerLabel,
        MinimaxModelRemains model,
        string rawJson,
        int httpStatus)
    {
        var usages = new List<ProviderUsage>();

        var modelName = model.ModelName ?? "Text Generation";
        var modelSlug = modelName.ToLowerInvariant().Replace(" ", "-", StringComparison.Ordinal);
        const int modelIndex = 0;

        // Token Plan uses remaining_percent when total count is 0.
        // Build synthetic total/remaining from percent when counts are absent.
        var burstTotal = model.IntervalTotal;
        var burstRemaining = model.IntervalRemaining;
        if (burstTotal <= 0 && model.IntervalRemainingPercent > 0)
        {
            burstTotal = 100;
            burstRemaining = model.IntervalRemainingPercent;
        }

        if (burstTotal > 0)
        {
            var burstPeriod = ResolvePeriodDuration(model.IntervalStartMs, model.IntervalEndMs);
            usages.Add(BuildModelWindowCard(new ModelWindowCardSpec(
                providerId,
                providerLabel,
                modelName,
                modelSlug,
                modelIndex,
                burstTotal,
                burstRemaining,
                model.IntervalEndMs,
                "burst",
                "5h",
                WindowKind.Burst,
                burstPeriod,
                rawJson,
                httpStatus)));
        }

        var weeklyTotal = model.WeeklyTotal;
        var weeklyRemaining = model.WeeklyRemaining;
        if (weeklyTotal <= 0 && model.WeeklyRemainingPercent > 0)
        {
            weeklyTotal = 100;
            weeklyRemaining = model.WeeklyRemainingPercent;
        }

        if (weeklyTotal > 0)
        {
            var weeklyPeriod = ResolvePeriodDuration(model.WeeklyStartMs, model.WeeklyEndMs);
            usages.Add(BuildModelWindowCard(new ModelWindowCardSpec(
                providerId,
                providerLabel,
                modelName,
                modelSlug,
                modelIndex,
                weeklyTotal,
                weeklyRemaining,
                model.WeeklyEndMs,
                "weekly",
                "Weekly",
                WindowKind.Rolling,
                weeklyPeriod,
                rawJson,
                httpStatus)));
        }

        return usages;
    }

    private static TimeSpan ResolvePeriodDuration(long startMs, long endMs)
    {
        if (startMs <= 0 || endMs <= startMs)
        {
            return TimeSpan.Zero;
        }

        return DateTimeOffset.FromUnixTimeMilliseconds(endMs) - DateTimeOffset.FromUnixTimeMilliseconds(startMs);
    }

    private static MinimaxModelRemains? FindTextGenerationModel(
        IReadOnlyList<MinimaxModelRemains> modelRemains)
    {
        var explicitTextGenerationModel = modelRemains.FirstOrDefault(model =>
            !string.IsNullOrWhiteSpace(model.ModelName) &&
            (model.ModelName.Contains(TextGenerationLabel, StringComparison.OrdinalIgnoreCase) ||
             model.ModelName.Contains(TextGenerationModelPrefix, StringComparison.OrdinalIgnoreCase)));

        if (explicitTextGenerationModel != null)
        {
            return explicitTextGenerationModel;
        }

        // Token Plan uses "general" as the model name for text generation credits.
        var generalModel = modelRemains.FirstOrDefault(model =>
            string.Equals(model.ModelName, GeneralModelName, StringComparison.OrdinalIgnoreCase));

        if (generalModel != null)
        {
            return generalModel;
        }

        // MiniMax Coding Plan recently shifted to names like "MiniMax-M*";
        // treat these as text-generation equivalents to preserve compatibility.
        return modelRemains.FirstOrDefault(model =>
            !string.IsNullOrWhiteSpace(model.ModelName) &&
            model.ModelName.Contains(CodingModelPrefix, StringComparison.OrdinalIgnoreCase));
    }

    private static ProviderUsage BuildModelWindowCard(ModelWindowCardSpec spec)
    {
        var remaining = Math.Max(0, Math.Min(spec.WindowRemaining, spec.WindowTotal));
        var used = spec.WindowTotal - remaining;
        var usedPct = Math.Clamp((used / spec.WindowTotal) * 100.0, 0, 100);
        var resetTime = spec.ResetMs > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(spec.ResetMs).UtcDateTime
            : (DateTime?)null;

        return new ProviderUsage
        {
            ProviderId = spec.ProviderId,
            ProviderName = spec.ProviderLabel,
            CardId = spec.ModelIndex == 0 ? spec.CardSuffix : $"{spec.ModelSlug}.{spec.CardSuffix}",
            GroupId = spec.ProviderId,
            Name = spec.ModelIndex == 0 ? spec.NameSuffix : $"{spec.ModelName} {spec.NameSuffix}",
            WindowKind = spec.WindowKind,
            UsedPercent = usedPct,
            RequestsUsed = used,
            RequestsAvailable = spec.WindowTotal,
            IsQuotaBased = true,
            PlanType = PlanType.Coding,
            IsAvailable = true,
            Description = $"{Math.Clamp(100.0 - usedPct, 0, 100).ToString("F0", CultureInfo.InvariantCulture)}% remaining ({spec.ModelName})",
            NextResetTime = resetTime,
            PeriodDuration = spec.PeriodDuration,
            RawJson = spec.RawJson,
            HttpStatus = spec.HttpStatus,
        };
    }

    private readonly record struct ModelWindowCardSpec(
        string ProviderId,
        string ProviderLabel,
        string ModelName,
        string ModelSlug,
        int ModelIndex,
        double WindowTotal,
        double WindowRemaining,
        long ResetMs,
        string CardSuffix,
        string NameSuffix,
        WindowKind WindowKind,
        TimeSpan PeriodDuration,
        string RawJson,
        int HttpStatus);

    private sealed class MinimaxRemainsResponse
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

        /// <summary>Gets or sets remaining count for the 5h window (misleadingly named "usage" in the API).</summary>
        [JsonPropertyName("current_interval_usage_count")]
        public double IntervalRemaining { get; set; }

        [JsonPropertyName("start_time")]
        public long IntervalStartMs { get; set; }

        [JsonPropertyName("end_time")]
        public long IntervalEndMs { get; set; }

        [JsonPropertyName("current_weekly_total_count")]
        public double WeeklyTotal { get; set; }

        /// <summary>Gets or sets remaining count for the weekly window (misleadingly named "usage" in the API).</summary>
        [JsonPropertyName("current_weekly_usage_count")]
        public double WeeklyRemaining { get; set; }

        [JsonPropertyName("weekly_start_time")]
        public long WeeklyStartMs { get; set; }

        [JsonPropertyName("weekly_end_time")]
        public long WeeklyEndMs { get; set; }

        [JsonPropertyName("current_interval_remaining_percent")]
        public double IntervalRemainingPercent { get; set; }

        [JsonPropertyName("current_weekly_remaining_percent")]
        public double WeeklyRemainingPercent { get; set; }

        [JsonPropertyName("remains_time")]
        public long RemainsTimeMs { get; set; }

        [JsonPropertyName("weekly_remains_time")]
        public long WeeklyRemainsTimeMs { get; set; }

        [JsonPropertyName("current_interval_status")]
        public int IntervalStatus { get; set; }

        [JsonPropertyName("current_weekly_status")]
        public int WeeklyStatus { get; set; }
    }
}
