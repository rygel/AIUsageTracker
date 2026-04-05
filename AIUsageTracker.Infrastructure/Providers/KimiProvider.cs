// <copyright file="KimiProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using AIUsageTracker.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class KimiProvider : ProviderBase
{
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
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            return new[] { this.CreateUnavailableUsage("API Key missing", authSource: config.AuthSource, state: ProviderUsageState.Missing) };
        }

        try
        {
            var request = CreateBearerRequest(HttpMethod.Get, "https://api.kimi.com/coding/v1/usages", config.ApiKey);

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

            double used = data.Usage.Used;
            double limit = data.Usage.Limit;
            double remaining = data.Usage.Remaining;

            DateTime? soonestResetDt = null;
            TimeSpan minDiff = TimeSpan.MaxValue;
            var flatCards = new List<ProviderUsage>();
            var usedCardIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Add weekly limit from usage only when data.Limits has no 7-day (Rolling) entry.
            var hasRollingFromLimits = data.Limits?.Any(l =>
                l.Window != null && DetermineWindowKind(l.Window.Duration, l.Window.TimeUnit) == WindowKind.Rolling) ?? false;

            if (limit > 0 && remaining >= 0 && !hasRollingFromLimits)
            {
                var weeklyUsedPct = UsageMath.CalculateUsedPercent(used, limit);
                DateTime? weeklyResetDt = null;
                if (!string.IsNullOrEmpty(data.Usage.ResetTime) &&
                    DateTime.TryParse(data.Usage.ResetTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var weeklyDt))
                {
                    weeklyResetDt = weeklyDt.ToUniversalTime();
                    var diff = weeklyResetDt.Value - DateTime.UtcNow;
                    if (diff.TotalSeconds > 0 && diff < minDiff)
                    {
                        minDiff = diff;
                        soonestResetDt = weeklyResetDt;
                    }
                }

                flatCards.Add(new ProviderUsage
                {
                    ProviderId = this.ProviderId,
                    ProviderName = this.Definition.DisplayName,
                    CardId = "weekly",
                    GroupId = this.ProviderId,
                    Name = "Weekly Limit",
                    WindowKind = WindowKind.Rolling,
                    UsedPercent = weeklyUsedPct,
                    RequestsUsed = used,
                    RequestsAvailable = limit,
                    IsQuotaBased = this.Definition.IsQuotaBased,
                    PlanType = this.Definition.PlanType,
                    IsAvailable = true,
                    Description = $"{remaining} remaining{(!string.IsNullOrEmpty(data.Usage.ResetTime) ? $" (Resets: {this.FormatResetTime(data.Usage.ResetTime)})" : string.Empty)}",
                    RawJson = content,
                    HttpStatus = (int)response.StatusCode,
                    NextResetTime = weeklyResetDt,
                    PeriodDuration = TimeSpan.FromDays(7),
                    AuthSource = config.AuthSource,
                });
                usedCardIds.Add("weekly");
            }

            if (data.Limits != null)
            {
                foreach (var limitItem in data.Limits)
                {
                    if (limitItem.Detail == null || limitItem.Window == null)
                    {
                        continue;
                    }

                    var win = limitItem.Window;
                    var det = limitItem.Detail;

                    if (det.Limit <= 0)
                    {
                        continue;
                    }

                    string name = $"{this.FormatDuration(win.Duration, win.TimeUnit ?? "TIME_UNIT_MINUTE")} Limit";
                    var itemUsed = det.Limit - det.Remaining;
                    var itemUsedPercentage = det.Limit > 0 ? (itemUsed / (double)det.Limit) * 100.0 : 0;

                    var resetDisplay = this.FormatResetTime(det.ResetTime ?? string.Empty);
                    DateTime? itemResetDt = null;
                    if (DateTime.TryParse(det.ResetTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    {
                        itemResetDt = dt.ToUniversalTime();
                        var diff = itemResetDt.Value - DateTime.UtcNow;
                        if (diff.TotalSeconds > 0 && diff < minDiff)
                        {
                            minDiff = diff;
                            soonestResetDt = itemResetDt;
                        }
                    }

                    var quotaBucketKind = DetermineWindowKind(win.Duration, win.TimeUnit);
                    var periodDuration = quotaBucketKind == WindowKind.Rolling
                        ? TimeSpan.FromDays(win.Duration)
                        : quotaBucketKind == WindowKind.Burst
                            ? (string.Equals(win.TimeUnit, "TIME_UNIT_HOUR", StringComparison.Ordinal)
                                ? TimeSpan.FromHours(win.Duration)
                                : TimeSpan.FromMinutes(win.Duration))
                            : (TimeSpan?)null;

                    var baseCardId = name.ToLowerInvariant()
                        .Replace(" ", "-", StringComparison.Ordinal)
                        .Replace("/", "-", StringComparison.Ordinal);
                    var cardId = baseCardId;
                    var dupCounter = 2;
                    while (!usedCardIds.Add(cardId))
                    {
                        cardId = $"{baseCardId}-{dupCounter}";
                        dupCounter++;
                    }

                    flatCards.Add(new ProviderUsage
                    {
                        ProviderId = this.ProviderId,
                        ProviderName = this.Definition.DisplayName,
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
                        Description = $"{det.Remaining} / {det.Limit} remaining (Resets: {resetDisplay})",
                        RawJson = content,
                        HttpStatus = (int)response.StatusCode,
                        NextResetTime = itemResetDt,
                        PeriodDuration = periodDuration,
                        AuthSource = config.AuthSource,
                    });
                }
            }

            if (flatCards.Count == 0)
            {
                return new[]
                {
                    new ProviderUsage
                    {
                        ProviderId = this.ProviderId,
                        ProviderName = this.Definition.DisplayName,
                        UsedPercent = limit > 0 ? UsageMath.CalculateUsedPercent(used, limit) : 0,
                        RequestsUsed = used,
                        RequestsAvailable = limit,
                        IsQuotaBased = this.Definition.IsQuotaBased,
                        PlanType = this.Definition.PlanType,
                        IsAvailable = true,
                        Description = "Active",
                        RawJson = content,
                        HttpStatus = (int)response.StatusCode,
                        NextResetTime = soonestResetDt,
                        AuthSource = config.AuthSource,
                    },
                };
            }

            return flatCards;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            this._logger.LogError(ex, "Kimi check failed");
            return new[] { this.CreateUnavailableUsage(DescribeUnavailableException(ex), authSource: config.AuthSource) };
        }
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

    private string FormatDuration(long duration, string unit)
    {
        return UsageWindowLabelFormatter.FormatDuration(duration, unit);
    }

    private string FormatResetTime(string resetTime)
    {
        if (DateTime.TryParse(resetTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return $"({dt:MMM dd HH:mm})";
        }

        return resetTime;
    }

    private class KimiUsageResponse
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
    private class KimiUsageData
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

    private class KimiLimitItem
    {
        [JsonPropertyName("window")]
        public KimiWindow? Window { get; set; }

        [JsonPropertyName("detail")]
        public KimiLimitDetail? Detail { get; set; }
    }

    private class KimiWindow
    {
        [JsonPropertyName("duration")]
        public long Duration { get; set; }

        [JsonPropertyName("timeUnit")]
        public string? TimeUnit { get; set; }
    }

    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    private class KimiLimitDetail
    {
        [JsonPropertyName("limit")]
        public long Limit { get; set; }

        [JsonPropertyName("remaining")]
        public long Remaining { get; set; }

        [JsonPropertyName("resetTime")]
        public string? ResetTime { get; set; }
    }
}
