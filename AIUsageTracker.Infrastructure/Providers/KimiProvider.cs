// <copyright file="KimiProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using AIUsageTracker.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class KimiProvider : ProviderBase
{
    private const string CodingUsagesEndpoint = "https://api.kimi.com/coding/v1/usages";

    private readonly HttpClient _httpClient;
    private readonly ILogger<KimiProvider> _logger;

    public KimiProvider(HttpClient httpClient, ILogger<KimiProvider> logger)
    {
        this._httpClient = httpClient;
        this._logger = logger;
    }

    public static ProviderDefinition StaticDefinition { get; } = new(
        "kimi-for-coding",
        "Kimi for Coding",
        PlanType.Coding,
        isQuotaBased: true)
    {
        AdditionalHandledProviderIds = new[] { "kimi" },
        DiscoveryEnvironmentVariables = new[] { "KIMI_API_KEY", "MOONSHOT_API_KEY" },
        IconAssetName = "kimi",
        BadgeColorHex = "#BA55D3",
        BadgeInitial = "K",
        QuotaWindows = new QuotaWindowDefinition[]
        {
            new(WindowKind.Burst,   "5h",     DetailName: "5h Limit",     PeriodDuration: TimeSpan.FromHours(5)),
            new(WindowKind.Burst,   "Daily",  DetailName: "1 Day Limit",  PeriodDuration: TimeSpan.FromDays(1)),
            new(WindowKind.Rolling, "Weekly", DetailName: "Weekly Limit", PeriodDuration: TimeSpan.FromDays(7)),
            new(WindowKind.Rolling, "Weekly", DetailName: "7 Day Limit",  PeriodDuration: TimeSpan.FromDays(7)),
        },
    };

    public override ProviderDefinition Definition => StaticDefinition;

    public override string ProviderId => StaticDefinition.ProviderId;

    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrEmpty(config.ApiKey))
        {
            return new[] { this.CreateUnavailableUsage("API Key missing", authSource: config.AuthSource, state: ProviderUsageState.Missing) };
        }

        try
        {
            var request = CreateBearerRequest(HttpMethod.Get, CodingUsagesEndpoint, config.ApiKey);

            var response = await this._httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                this._logger.LogWarning("Failed to fetch Kimi usage: {StatusCode}", response.StatusCode);
                return new[] { this.CreateUnavailableUsage(DescribeUnavailableStatus(response.StatusCode), (int)response.StatusCode, authSource: config.AuthSource) };
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            KimiUsageResponse? data;
            try
            {
                data = JsonSerializer.Deserialize<KimiUsageResponse>(content, JsonOptions);
            }
            catch (JsonException ex)
            {
                this._logger.LogError(
                    ex,
                    "Kimi API response could not be deserialized. Unexpected format? Raw: {Raw}",
                    content.Length > 500 ? content[..500] : content);
                return new[] { this.CreateUnavailableUsage($"Failed to parse response: {ex.Message}", authSource: config.AuthSource) };
            }

            if (data == null || data.Usage == null)
            {
                return new[] { this.CreateUnavailableUsage("Response missing usage data", authSource: config.AuthSource) };
            }

            return this.BuildUsageCards(data, content, (int)response.StatusCode, config.AuthSource, ProviderMetadataCatalog.GetConfiguredDisplayName(config.ProviderId));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            this._logger.LogError(ex, "Kimi check failed");
            return new[] { this.CreateUnavailableUsage(DescribeUnavailableException(ex), authSource: config.AuthSource) };
        }
    }

    private IEnumerable<ProviderUsage> BuildUsageCards(KimiUsageResponse data, string content, int statusCode, string? authSource, string providerLabel)
    {
        var flatCards = new List<ProviderUsage>();
        var usedCardIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var hasRollingFromLimits = data.Limits?.Any(l =>
            l.Window != null && DetermineWindowKind(l.Window.Duration, l.Window.TimeUnit) == WindowKind.Rolling) ?? false;

        var weeklyCard = this.TryBuildWeeklyCard(data.Usage!, hasRollingFromLimits, content, statusCode, authSource, providerLabel);
        if (weeklyCard != null)
        {
            flatCards.Add(weeklyCard);
            usedCardIds.Add("weekly");
        }

        if (data.Limits != null)
        {
            this.AddLimitCards(data.Limits, flatCards, usedCardIds, content, statusCode, authSource, providerLabel);
        }

        if (flatCards.Count == 0)
        {
            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = this.ProviderId,
                    ProviderName = providerLabel,
                    UsedPercent = data.Usage!.Limit > 0 ? UsageMath.CalculateUsedPercent(data.Usage!.Used, data.Usage!.Limit) : 0,
                    RequestsUsed = data.Usage!.Used,
                    RequestsAvailable = data.Usage!.Limit,
                    IsQuotaBased = this.Definition.IsQuotaBased,
                    PlanType = this.Definition.PlanType,
                    IsAvailable = true,
                    Description = "Active",
                    RawJson = content,
                    HttpStatus = statusCode,
                    AuthSource = authSource ?? string.Empty,
                },
            };
        }

        return flatCards;
    }

    private ProviderUsage? TryBuildWeeklyCard(KimiUsageData usage, bool hasRollingFromLimits, string content, int statusCode, string? authSource, string providerLabel)
    {
        if (usage.Limit <= 0 || usage.Remaining < 0 || hasRollingFromLimits)
        {
            return null;
        }

        var weeklyUsedPct = UsageMath.CalculateUsedPercent(usage.Used, usage.Limit);
        DateTime? weeklyResetDt = null;
        if (!string.IsNullOrEmpty(usage.ResetTime) &&
            DateTime.TryParse(usage.ResetTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var weeklyDt))
        {
            weeklyResetDt = weeklyDt.ToUniversalTime();
        }

        return new ProviderUsage
        {
            ProviderId = this.ProviderId,
            ProviderName = providerLabel,
            CardId = "weekly",
            GroupId = this.ProviderId,
            Name = "Weekly Limit",
            WindowKind = WindowKind.Rolling,
            UsedPercent = weeklyUsedPct,
            RequestsUsed = usage.Used,
            RequestsAvailable = usage.Limit,
            IsQuotaBased = this.Definition.IsQuotaBased,
            PlanType = this.Definition.PlanType,
            IsAvailable = true,
            Description = $"{usage.Remaining.ToString(CultureInfo.InvariantCulture)} remaining{(!string.IsNullOrEmpty(usage.ResetTime) ? $" (Resets: {FormatResetTime(usage.ResetTime)})" : string.Empty)}",
            RawJson = content,
            HttpStatus = statusCode,
            NextResetTime = weeklyResetDt,
            PeriodDuration = TimeSpan.FromDays(7),
            AuthSource = authSource ?? string.Empty,
        };
    }

    private void AddLimitCards(List<KimiLimitItem> limits, List<ProviderUsage> flatCards, HashSet<string> usedCardIds, string content, int statusCode, string? authSource, string providerLabel)
    {
        foreach (var limitItem in limits)
        {
            var card = this.TryBuildLimitCard(limitItem, usedCardIds, content, statusCode, authSource, providerLabel);
            if (card != null)
            {
                flatCards.Add(card);
            }
        }
    }

    private ProviderUsage? TryBuildLimitCard(KimiLimitItem limitItem, HashSet<string> usedCardIds, string content, int statusCode, string? authSource, string providerLabel)
    {
        if (limitItem.Detail == null || limitItem.Window == null)
        {
            return null;
        }

        var win = limitItem.Window;
        var det = limitItem.Detail;

        if (det.Limit <= 0)
        {
            return null;
        }

        string name = $"{FormatDuration(win.Duration, win.TimeUnit ?? "TIME_UNIT_MINUTE")} Limit";
        var itemUsed = det.Limit - det.Remaining;
        var itemUsedPercentage = det.Limit > 0 ? (itemUsed / (double)det.Limit) * 100.0 : 0;

        var resetDisplay = FormatResetTime(det.ResetTime ?? string.Empty);
        DateTime? itemResetDt = null;
        if (DateTime.TryParse(det.ResetTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            itemResetDt = dt.ToUniversalTime();
        }

        var quotaBucketKind = DetermineWindowKind(win.Duration, win.TimeUnit);
        var periodDuration = ResolvePeriodDuration(quotaBucketKind, win);
        var cardId = DeduplicateCardId(name, usedCardIds);

        return new ProviderUsage
        {
            ProviderId = this.ProviderId,
            ProviderName = providerLabel,
            CardId = cardId,
            GroupId = this.ProviderId,
            Name = name,
            WindowKind = quotaBucketKind,
            UsedPercent = itemUsedPercentage,
            RequestsUsed = itemUsed,
            RequestsAvailable = det.Limit,
            IsQuotaBased = this.Definition.IsQuotaBased,
            PlanType = this.Definition.PlanType,
            IsAvailable = true,
            Description = $"{det.Remaining.ToString(CultureInfo.InvariantCulture)} / {det.Limit.ToString(CultureInfo.InvariantCulture)} remaining (Resets: {resetDisplay})",
            RawJson = content,
            HttpStatus = statusCode,
            NextResetTime = itemResetDt,
            PeriodDuration = periodDuration,
            AuthSource = authSource ?? string.Empty,
        };
    }

    private static string DeduplicateCardId(string name, HashSet<string> usedCardIds)
    {
        var baseCardId = name.ToLowerInvariant()
            .Replace(" ", "-", StringComparison.Ordinal)
            .Replace("/", "-", StringComparison.Ordinal);
        var cardId = baseCardId;
        var dupCounter = 2;
        while (!usedCardIds.Add(cardId))
        {
            cardId = $"{baseCardId}-{dupCounter.ToString(CultureInfo.InvariantCulture)}";
            dupCounter++;
        }

        return cardId;
    }

    private static TimeSpan? ResolvePeriodDuration(WindowKind quotaBucketKind, KimiWindow win)
    {
        if (quotaBucketKind == WindowKind.Rolling)
        {
            return TimeSpan.FromDays(win.Duration);
        }

        if (quotaBucketKind == WindowKind.Burst)
        {
            return string.Equals(win.TimeUnit, "TIME_UNIT_HOUR", StringComparison.Ordinal)
                ? TimeSpan.FromHours(win.Duration)
                : TimeSpan.FromMinutes(win.Duration);
        }

        return null;
    }

    private static WindowKind DetermineWindowKind(long duration, string? unit)
    {
        if (string.Equals(unit, "TIME_UNIT_DAY", StringComparison.Ordinal) && duration >= 7)
        {
            return WindowKind.Rolling;
        }

        // Daily limits in some coding plans
        if (string.Equals(unit, "TIME_UNIT_DAY", StringComparison.Ordinal) && duration == 1)
        {
            return WindowKind.Burst;
        }

        // 3h or 5h windows should be Primary
        if (string.Equals(unit, "TIME_UNIT_HOUR", StringComparison.Ordinal) && (duration == 3 || duration == 5))
        {
            return WindowKind.Burst;
        }

        // Minutes-based windows (like 60m for 1h, 180m for 3h, 300m for 5h)
        if (string.Equals(unit, "TIME_UNIT_MINUTE", StringComparison.Ordinal) && (duration >= 60 && duration <= 300))
        {
            return WindowKind.Burst;
        }

        return WindowKind.None;
    }

    private static string FormatDuration(long duration, string unit)
    {
        return UsageWindowLabelFormatter.FormatDuration(duration, unit);
    }

    private static string FormatResetTime(string resetTime)
    {
        if (DateTime.TryParse(resetTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return $"({dt.ToString("MMM dd HH:mm", CultureInfo.InvariantCulture)})";
        }

        return resetTime;
    }

    private sealed class KimiUsageResponse
    {
        [JsonPropertyName("usage")]
        public KimiUsageData? Usage { get; set; }

        [JsonPropertyName("limits")]
        public List<KimiLimitItem>? Limits { get; set; }
    }

    // Kimi API intentionally returns numeric fields as JSON strings (e.g. {"limit":"100"}).
    // [JsonNumberHandling] is applied explicitly here — this is a documented API contract,
    // not a global silent fallback. If Kimi's format changes, JsonException will be thrown
    // and logged by the caller.
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    private sealed class KimiUsageData
    {
        [JsonPropertyName("limit")]
        public long Limit { get; set; }

        [JsonPropertyName("used")]
        public long Used { get; set; }

        [JsonPropertyName("remaining")]
        public long Remaining { get; set; }

        [JsonPropertyName("resetTime")]
        public string? ResetTime { get; set; }
    }

    private sealed class KimiLimitItem
    {
        [JsonPropertyName("window")]
        public KimiWindow? Window { get; set; }

        [JsonPropertyName("detail")]
        public KimiLimitDetail? Detail { get; set; }
    }

    private sealed class KimiWindow
    {
        [JsonPropertyName("duration")]
        public long Duration { get; set; }

        [JsonPropertyName("timeUnit")]
        public string? TimeUnit { get; set; }
    }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    private sealed class KimiLimitDetail
    {
        [JsonPropertyName("limit")]
        public long Limit { get; set; }

        [JsonPropertyName("remaining")]
        public long Remaining { get; set; }

        [JsonPropertyName("resetTime")]
        public string? ResetTime { get; set; }
    }
}
