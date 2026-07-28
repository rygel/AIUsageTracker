// <copyright file="ZaiProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class ZaiProvider : ProviderBase
{
    private const string QuotaLimitEndpoint = "https://api.z.ai/api/monitor/usage/quota/limit";

    private readonly HttpClient _httpClient;
    private readonly ILogger<ZaiProvider> _logger;

    public ZaiProvider(HttpClient httpClient, ILogger<ZaiProvider> logger)
    {
        this._httpClient = httpClient;
        this._logger = logger;
    }

    public static ProviderDefinition StaticDefinition { get; } = new(
        "zai-coding-plan",
        "Z.ai Coding Plan",
        PlanType.Coding,
        isQuotaBased: true)
    {
        AdditionalHandledProviderIds = new[] { "zai" },
        DisplayNameOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["zai"] = "Z.AI",
        },
        DiscoveryEnvironmentVariables = new[] { "ZAI_API_KEY", "Z_AI_API_KEY" },
        RooConfigPropertyNames = new[] { "zaiApiKey" },
        IconAssetName = "zai",
        BadgeColorHex = "#20B2AA",
        BadgeInitial = "Z",
        QuotaWindows = new QuotaWindowDefinition[]
        {
            new(WindowKind.Burst,   "5h",     PeriodDuration: TimeSpan.FromHours(5)),
            new(WindowKind.Rolling, "Weekly", PeriodDuration: TimeSpan.FromDays(7)),
        },
    };

    /// <inheritdoc/>
    public override ProviderDefinition Definition => StaticDefinition;

    /// <inheritdoc/>
    public override string ProviderId => StaticDefinition.ProviderId;

    /// <inheritdoc/>
    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrEmpty(config.ApiKey))
        {
            throw new ArgumentException("API Key not found for Z.AI provider.", nameof(config));
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, QuotaLimitEndpoint);

        // Z.AI uses raw key in Authorization header without "Bearer" prefix based on Swift ref
        request.Headers.TryAddWithoutValidation("Authorization", config.ApiKey);
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en");

        this._logger.LogDebug("[ZAI] Sending API request to https://api.z.ai/api/monitor/usage/quota/limit");
        var response = await this._httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var httpStatus = (int)response.StatusCode;

        if (!response.IsSuccessStatusCode)
        {
            this._logger.LogError("[ZAI] API returned {StatusCode}", response.StatusCode);
            throw new HttpRequestException($"Z.AI API returned {response.StatusCode}");
        }

        var responseString = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        this._logger.LogDebug("[ZAI RAW RESPONSE] {Response}", responseString);

        // Parse envelope
        var envelope = JsonSerializer.Deserialize<ZaiEnvelope<ZaiQuotaLimitResponse>>(responseString);

        var limits = envelope?.Data?.Limits;
        this._logger.LogDebug("[ZAI] Parsed envelope - Data is null: {IsNull}, Limits count: {Count}", envelope?.Data == null, limits?.Count ?? 0);

        if (limits == null || limits.Count == 0)
        {
            return this.BuildWindowInactiveResult(config, responseString, httpStatus);
        }

        foreach (var limit in limits)
        {
            this._logger.LogDebug(
                "[ZAI LIMIT] Type={Type} Total={Total} CurrentValue={Current} Remaining={Remaining} Percentage={Pct} ResetTime={Ts}",
                limit.Type,
                limit.Total,
                limit.CurrentValue,
                limit.Remaining,
                limit.Percentage,
                limit.NextResetTime);
        }

        var results = new List<ProviderUsage>();

        // Process each distinct TOKENS_LIMIT window. As of 2026-07-27 the live API
        // returns up to two token windows: a 5-hour rolling (unit=3, number=5) and
        // a weekly rolling GLM quota (unit=6, number=1). Each window gets its own
        // card; we never silently drop a window (regression: previously the Weekly
        // window at 100% was hidden behind the 5h fresh window).
        foreach (var tokenLimit in limits.Where(l =>
            l.Type != null && (l.Type.Equals("TOKENS_LIMIT", StringComparison.OrdinalIgnoreCase) ||
                               l.Type.Equals("Tokens", StringComparison.OrdinalIgnoreCase))))
        {
            var window = ClassifyTokenWindow(tokenLimit);
            this._logger.LogDebug(
                "[ZAI] Processing TOKENS_LIMIT window={Window} unit={Unit} number={Number} percentage={Pct}",
                window.Label,
                tokenLimit.Unit,
                tokenLimit.Number,
                tokenLimit.Percentage);

            var tokenResult = this.ProcessTokenLimit(tokenLimit);
            if (tokenResult.RemainingPercent.HasValue)
            {
                var (nextReset, resetStr) = this.ResolveResetTimeInfo(tokenLimit, window.Duration, limits);
                results.Add(this.BuildWindowedUsageResult(
                    tokenResult,
                    tokenLimit,
                    config,
                    responseString,
                    httpStatus,
                    window,
                    nextReset,
                    resetStr));
            }
            else
            {
                results.AddRange(this.BuildUnknownUsageResult(config, responseString, httpStatus));
            }
        }

        // Process TIME_LIMIT (monthly web search / reader / zread quota) as separate card using "zai" provider ID
        var timeLimit = limits.FirstOrDefault(static l =>
            l.Type != null && (l.Type.Equals("TIME_LIMIT", StringComparison.OrdinalIgnoreCase) ||
                                l.Type.Equals("Time", StringComparison.OrdinalIgnoreCase)));
        if (timeLimit != null && timeLimit.Percentage.HasValue)
        {
            this._logger.LogDebug("[ZAI] Processing TIME_LIMIT as separate card");
            results.Add(this.BuildTimeLimitResult(timeLimit, config, responseString, httpStatus));
        }

        if (results.Count == 0)
        {
            return this.BuildUnknownUsageResult(config, responseString, httpStatus);
        }

        return results;
    }

    private ProviderUsage[] BuildNoLimitsResult(ProviderConfig config, string responseString, int httpStatus)
    {
        this._logger.LogDebug("[ZAI] No limits found in response");
        var label = ProviderMetadataCatalog.GetConfiguredDisplayName(config.ProviderId);
        return new[]
        {
            new QuotaProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = label,
                IsAvailable = false,
                Description = "No usage data available",
                IsQuotaBased = this.Definition.IsQuotaBased,
                PlanType = this.Definition.PlanType,
                RawJson = responseString,
                HttpStatus = httpStatus,
            },
        };
    }

    private ProviderUsage[] BuildWindowInactiveResult(ProviderConfig config, string responseString, int httpStatus)
    {
        this._logger.LogDebug("[ZAI] Response successful but `data` is empty — quota window inactive (5h rolling reset)");
        var label = ProviderMetadataCatalog.GetConfiguredDisplayName(config.ProviderId);
        return new[]
        {
            new QuotaProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = label,
                IsAvailable = true,
                Description = "Quota window inactive (5h rolling)",
                IsQuotaBased = this.Definition.IsQuotaBased,
                PlanType = this.Definition.PlanType,
                UsedPercent = 0,
                RequestsUsed = 0,
                RequestsAvailable = 0,
                RawJson = responseString,
                HttpStatus = httpStatus,
                UpstreamResponseValidity = UpstreamResponseValidity.Valid,
            },
        };
    }

    private ZaiQuotaLimitItem? SelectTokenLimit(List<ZaiQuotaLimitItem> limits)
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var tokenLimits = limits.Where(l =>
            l.Type != null && (l.Type.Equals("TOKENS_LIMIT", StringComparison.OrdinalIgnoreCase) ||
                               l.Type.Equals("Tokens", StringComparison.OrdinalIgnoreCase))).ToList();

        return tokenLimits.Where(l => IsFutureTimestamp(l.NextResetTime, nowMs, nowSec))
                          .OrderBy(l => l.Remaining ?? long.MaxValue)
                          .FirstOrDefault()
                ?? tokenLimits.FirstOrDefault();
    }

    private readonly record struct TokenWindowClassification(
        string CardId,
        string Label,
        WindowKind Kind,
        TimeSpan Duration);

    /// <summary>
    /// Maps a Z.AI TOKENS_LIMIT row to one of the declared quota windows. The (unit, number)
    /// pair on the API tells us which window this row represents:
    /// <list type="bullet">
    ///   <item><c>unit=3, number=5</c> — 5-hour rolling burst window</item>
    ///   <item><c>unit=6, number=1</c> — 1-week rolling GLM quota (added 2026-07-24)</item>
    ///   <item>no unit/number — legacy fallback, treated as 5h (most common historical pattern)</item>
    /// </list>
    /// </summary>
    private static TokenWindowClassification ClassifyTokenWindow(ZaiQuotaLimitItem limit)
    {
        return (limit.Unit, limit.Number) switch
        {
            (3, 5) => new TokenWindowClassification("5h", "5h", WindowKind.Burst, TimeSpan.FromHours(5)),
            (6, 1) => new TokenWindowClassification("weekly", "Weekly", WindowKind.Rolling, TimeSpan.FromDays(7)),

            // Future-proof variants observed in other providers; harmless if never sent.
            (4, 7) => new TokenWindowClassification("weekly", "Weekly", WindowKind.Rolling, TimeSpan.FromDays(7)),
            (5, 1) => new TokenWindowClassification("monthly", "Monthly", WindowKind.Rolling, TimeSpan.FromDays(30)),
            _ => new TokenWindowClassification("5h", "5h", WindowKind.Burst, TimeSpan.FromHours(5)),
        };
    }

    private ProviderUsage[] BuildUnknownUsageResult(ProviderConfig config, string responseString, int httpStatus)
    {
        var label = ProviderMetadataCatalog.GetConfiguredDisplayName(config.ProviderId);
        return new[]
        {
            new QuotaProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = label,
                IsAvailable = false,
                Description = "Usage unknown (missing quota metrics)",
                IsQuotaBased = this.Definition.IsQuotaBased,
                PlanType = this.Definition.PlanType,
                RawJson = responseString,
                HttpStatus = httpStatus,
            },
        };
    }

    private ProviderUsage BuildWindowedUsageResult(
        TokenLimitResult tokenResult,
        ZaiQuotaLimitItem tokenLimit,
        ProviderConfig config,
        string responseString,
        int httpStatus,
        TokenWindowClassification window,
        DateTime? nextResetTime,
        string resetStr)
    {
        var label = ProviderMetadataCatalog.GetConfiguredDisplayName(config.ProviderId);
        var finalRemainingPercent = Math.Min(tokenResult.RemainingPercent!.Value, 100);
        var finalUsedPercent = Math.Max(0, 100.0 - finalRemainingPercent);

        tokenResult = !tokenResult.HasRawLimitData
            ? tokenResult with
            {
                RequestsAvailable = 100,
                RequestsUsed = Math.Max(0, 100 - finalRemainingPercent),
            }
            : tokenResult;

        // Description: prefer the detailed "X% Remaining of YM tokens limit | Plan: X" format
        // when raw counts are present. Weekly rows in the live API only carry `percentage`,
        // so fall back to a simple "X% remaining" message in that case.
        string baseDescription;
        if (tokenResult.HasRawLimitData && !string.IsNullOrEmpty(tokenResult.DetailInfo))
        {
            baseDescription = tokenResult.DetailInfo + resetStr;
        }
        else
        {
            baseDescription = tokenResult.HasRawLimitData
                ? (tokenResult.DetailInfo + resetStr)
                : $"{finalRemainingPercent.ToString("F1", CultureInfo.InvariantCulture)}% remaining{resetStr}";
        }

        var finalDescription = FormatDescription(baseDescription, tokenResult.PlanDescription);

        this._logger.LogInformation(
            "Z.AI {Label} - UsedPercent: {UsedPercent}%, RequestsUsed: {RequestsUsed}, Description: {Description}",
            window.Label,
            finalUsedPercent,
            tokenResult.RequestsUsed,
            finalDescription);

        return new WindowedProviderUsage
        {
            ProviderId = this.ProviderId,
            ProviderName = label,
            UsedPercent = finalUsedPercent,
            RequestsUsed = tokenResult.RequestsUsed,
            RequestsAvailable = tokenResult.RequestsAvailable,
            IsQuotaBased = this.Definition.IsQuotaBased,
            PlanType = this.Definition.PlanType,
            DisplayAsFraction = tokenResult.RequestsAvailable > 100,
            WindowKind = window.Kind,
            PeriodDuration = window.Duration,
            CardId = window.CardId,
            GroupId = this.ProviderId,
            Name = window.Label,
            Description = finalDescription,
            NextResetTime = nextResetTime,
            IsAvailable = true,
            RawJson = responseString,
            HttpStatus = httpStatus,
        };
    }

    private ProviderUsage BuildTimeLimitResult(ZaiQuotaLimitItem timeLimit, ProviderConfig config, string responseString, int httpStatus)
    {
        var label = ProviderMetadataCatalog.GetConfiguredDisplayName("zai");
        double usedPercent = timeLimit.Percentage!.Value;
        double remainingPercent = Math.Max(0, 100 - usedPercent);

        var usageSummary = timeLimit.UsageDetails != null && timeLimit.UsageDetails.Count > 0
            ? string.Join(", ", timeLimit.UsageDetails.Select(d =>
                $"{d.ModelCode}: {d.Usage}"))
            : null;

        DateTime? nextResetTime = null;
        string resetStr;
        if (timeLimit.NextResetTime.HasValue && timeLimit.NextResetTime.Value > 0)
        {
            nextResetTime = ParseTimestamp(timeLimit.NextResetTime.Value);
            resetStr = $" (Resets: {nextResetTime!.Value.ToString("MMM dd, yyyy HH:mm", CultureInfo.InvariantCulture)} Local)";
        }
        else
        {
            resetStr = string.Empty;
        }

        var detailLine = usageSummary != null
            ? $"Web Search & Reader: {remainingPercent.ToString("F1", CultureInfo.InvariantCulture)}% remaining ({timeLimit.CurrentValue ?? 0}/{timeLimit.Total ?? 0}){resetStr}"
            : $"Monthly quota: {remainingPercent.ToString("F1", CultureInfo.InvariantCulture)}% remaining{resetStr}";

        this._logger.LogInformation(
            "Z.AI TIME_LIMIT - UsedPercent: {UsedPercent}%, Description: {Description}",
            usedPercent,
            detailLine);

        return new QuotaProviderUsage
        {
            ProviderId = "zai",
            ProviderName = label,
            UsedPercent = usedPercent,
            RequestsUsed = timeLimit.CurrentValue ?? 0,
            RequestsAvailable = timeLimit.Total ?? 0,
            IsQuotaBased = true,
            PlanType = PlanType.Usage,
            DisplayAsFraction = (timeLimit.Total ?? 0) > 100,
            Description = detailLine,
            NextResetTime = nextResetTime,
            IsAvailable = true,
            RawJson = responseString,
            HttpStatus = httpStatus,
        };
    }

    private readonly record struct TokenLimitResult(
        double? RemainingPercent,
        string DetailInfo,
        string PlanDescription,
        double RequestsAvailable,
        double RequestsUsed,
        bool HasRawLimitData);

    private TokenLimitResult ProcessTokenLimit(ZaiQuotaLimitItem? tokenLimit)
    {
        if (tokenLimit == null)
        {
            return default;
        }

        var planDescription = "Coding Plan";
        this._logger.LogDebug(
            "[ZAI] Processing TOKENS_LIMIT - Percentage={Pct} Total={Total} Current={Current} Remaining={Remaining}",
            tokenLimit.Percentage,
            tokenLimit.Total,
            tokenLimit.CurrentValue,
            tokenLimit.Remaining);

        if (tokenLimit.Percentage.HasValue && tokenLimit.Total == null && tokenLimit.CurrentValue == null && tokenLimit.Remaining == null)
        {
            double usedPercent = tokenLimit.Percentage.Value;
            double remainingPercentVal = 100 - usedPercent;
            return new TokenLimitResult(
                RemainingPercent: remainingPercentVal,
                DetailInfo: $"{remainingPercentVal.ToString("F1", CultureInfo.InvariantCulture)}% Remaining",
                PlanDescription: planDescription,
                RequestsAvailable: 100,
                RequestsUsed: 100 - remainingPercentVal,
                HasRawLimitData: false);
        }

        double totalVal = tokenLimit.Total ?? 0;
        double usedVal = tokenLimit.CurrentValue ?? 0;
        double remainingVal = tokenLimit.Remaining ?? (totalVal - usedVal);

        if (tokenLimit.Total > 50000000)
        {
            planDescription = "Coding Plan (Ultra/Enterprise)";
        }
        else if (tokenLimit.Total > 10000000)
        {
            planDescription = "Coding Plan (Pro)";
        }

        if (totalVal > 0)
        {
            double remainingPercentVal = (remainingVal / totalVal) * 100.0;
            this._logger.LogDebug(
                "[ZAI] Calculation: Remaining={RemainingVal} / Total={TotalVal} = {Percent}%",
                remainingVal,
                totalVal,
                remainingPercentVal);

            return new TokenLimitResult(
                RemainingPercent: remainingPercentVal,
                DetailInfo: $"{remainingPercentVal.ToString("F1", CultureInfo.InvariantCulture)}% Remaining of {(totalVal / 1000000.0).ToString("F0", CultureInfo.InvariantCulture)}M tokens limit",
                PlanDescription: planDescription,
                RequestsAvailable: totalVal,
                RequestsUsed: usedVal,
                HasRawLimitData: true);
        }

        if (tokenLimit.Percentage.HasValue)
        {
            double usedPercent = tokenLimit.Percentage.Value;
            double remainingPercentVal = 100 - usedPercent;
            return new TokenLimitResult(
                RemainingPercent: remainingPercentVal,
                DetailInfo: $"{remainingPercentVal.ToString("F1", CultureInfo.InvariantCulture)}% Remaining",
                PlanDescription: planDescription,
                RequestsAvailable: 100,
                RequestsUsed: 100 - remainingPercentVal,
                HasRawLimitData: false);
        }

        this._logger.LogDebug("[ZAI] Token limit missing usable quota metrics; usage unknown");
        return default;
    }

    private static bool IsFutureTimestamp(long? resetTime, long nowMs, long nowSec)
    {
        if (!resetTime.HasValue || resetTime.Value == 0)
        {
            return true;
        }

        var ts = resetTime.Value;
        return ts > 10000000000 ? ts > nowMs : ts > nowSec;
    }

    private (DateTime? ResetTime, string ResetString) ResolveResetTimeInfo(
        ZaiQuotaLimitItem? tokenLimit, TimeSpan? tokenWindowDuration, List<ZaiQuotaLimitItem> limits)
    {
        var tokenWindowLabel = tokenWindowDuration.HasValue
            ? $"{((int)tokenWindowDuration.Value.TotalHours).ToString(CultureInfo.InvariantCulture)}h window"
            : null;

        if (tokenLimit != null && tokenLimit.Percentage > 0 && tokenLimit.NextResetTime.HasValue && tokenLimit.NextResetTime.Value > 0)
        {
            var ts = tokenLimit.NextResetTime.Value;
            this._logger.LogDebug("[ZAI] Active token window reset timestamp: {Ts}", ts);
            var nextResetTime = ParseTimestamp(ts);
            return (nextResetTime, $" (Resets: {nextResetTime.ToString("MMM dd, yyyy HH:mm", CultureInfo.InvariantCulture)} Local)");
        }

        if (tokenWindowLabel != null)
        {
            return (null, $" ({tokenWindowLabel})");
        }

        var limitWithReset = limits
            .Where(l => l.NextResetTime.HasValue && l.NextResetTime.Value > 0)
            .OrderBy(l => l.NextResetTime!.Value)
            .FirstOrDefault();
        if (limitWithReset != null)
        {
            this._logger.LogDebug("[ZAI] Fallback reset timestamp: {Ts}", limitWithReset.NextResetTime!.Value);
            var nextResetTime = ParseTimestamp(limitWithReset.NextResetTime!.Value);
            return (nextResetTime, $" (Resets: {nextResetTime.ToString("MMM dd, yyyy HH:mm", CultureInfo.InvariantCulture)} Local)");
        }

        return (null, string.Empty);
    }

    private static string FormatDescription(string description, string planDescription)
    {
        return string.Equals(planDescription, "API", StringComparison.OrdinalIgnoreCase)
            ? description
            : $"{description} | Plan: {planDescription}";
    }

    private static DateTime ParseTimestamp(long ts)
    {
        // Heuristic: values > 10^10 are milliseconds (e.g. 1773454046559), otherwise seconds.
        return ts < 10_000_000_000
            ? DateTimeOffset.FromUnixTimeSeconds(ts).LocalDateTime
            : DateTimeOffset.FromUnixTimeMilliseconds(ts).LocalDateTime;
    }

    private sealed class ZaiEnvelope<T>
    {
        [JsonPropertyName("data")]
        public T? Data { get; set; }
    }

    private sealed class ZaiQuotaLimitResponse
    {
        [JsonPropertyName("limits")]
        public List<ZaiQuotaLimitItem>? Limits { get; set; }
    }

    private sealed class ZaiQuotaLimitItem
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("percentage")]
        public double? Percentage { get; set; }

        [JsonPropertyName("currentValue")]
        public long? CurrentValue { get; set; }

        [JsonPropertyName("usage")] // This is actually the total limit in Z.AI response
        public long? Total { get; set; }

        [JsonPropertyName("remaining")]
        public long? Remaining { get; set; }

        [JsonPropertyName("nextResetTime")]
        public long? NextResetTime { get; set; }

        /// <summary>
        /// Gets or sets z.ai window duration unit. Observed values: 3 = hours, 5 = months.
        /// Coding Plan TOKENS_LIMIT: unit=3, number=5 → 5-hour rolling window.
        /// </summary>
        [JsonPropertyName("unit")]
        public int? Unit { get; set; }

        /// <summary>Gets or sets number of units in the window duration (e.g. 5 for a 5-hour window).</summary>
        [JsonPropertyName("number")]
        public long? Number { get; set; }

        [JsonPropertyName("usageDetails")]
        public List<ZaiUsageDetail>? UsageDetails { get; set; }
    }

    private sealed class ZaiUsageDetail
    {
        [JsonPropertyName("modelCode")]
        public string ModelCode { get; set; } = string.Empty;

        [JsonPropertyName("usage")]
        public long Usage { get; set; }
    }
}
