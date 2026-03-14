// <copyright file="KimiProvider.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

#pragma warning disable CS0618 // RequestsPercentage: provider sets raw serialized field

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
        providerId: "kimi-for-coding",
        displayName: "Kimi for Coding",
        planType: PlanType.Coding,
        isQuotaBased: true,
        defaultConfigType: "quota-based",
        includeInWellKnownProviders: true,
        handledProviderIds: new[] { "kimi" },
        discoveryEnvironmentVariables: new[] { "KIMI_API_KEY", "MOONSHOT_API_KEY" },
        iconAssetName: "kimi",
        fallbackBadgeColorHex: "#BA55D3",
        fallbackBadgeInitial: "K");

    public override ProviderDefinition Definition => StaticDefinition;

    public override string ProviderId => StaticDefinition.ProviderId;

    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            return new[] { this.CreateUnavailableUsage("API Key missing", authSource: config.AuthSource) };
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.kimi.com/coding/v1/usages");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);

            var response = await this._httpClient.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                this._logger.LogWarning("Failed to fetch Kimi usage: {StatusCode}", response.StatusCode);
                return new[] { this.CreateUnavailableUsageFromStatus(response, authSource: config.AuthSource) };
            }

            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
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

            var remainingPercentage = limit > 0
                ? UsageMath.CalculateRemainingPercent(used, limit)
                : 100.0;

            var description = "Active";

            // Limits Detail
            string soonestResetStr = string.Empty;
            DateTime? soonestResetDt = null;
            var details = new List<ProviderUsageDetail>();
            TimeSpan minDiff = TimeSpan.MaxValue;

            // Add weekly limit from usage as Secondary detail (always, as this is the primary quota)
            if (limit > 0 && remaining >= 0)
            {
                var weeklyUsedPct = UsageMath.CalculateUsedPercent(used, limit);
                DateTime? weeklyResetDt = null;
                if (!string.IsNullOrEmpty(data.Usage.ResetTime) &&
                    DateTime.TryParse(data.Usage.ResetTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var weeklyDt))
                {
                    weeklyResetDt = weeklyDt.ToLocalTime();
                    var diff = weeklyResetDt.Value - DateTime.Now;
                    if (diff.TotalSeconds > 0 && diff < minDiff)
                    {
                        minDiff = diff;
                        soonestResetDt = weeklyResetDt;
                    }
                }

                details.Add(new ProviderUsageDetail
                {
                    Name = "Weekly Limit",
                    Description = $"{remaining} remaining{(!string.IsNullOrEmpty(data.Usage.ResetTime) ? $" (Resets: {this.FormatResetTime(data.Usage.ResetTime)})" : string.Empty)}",
                    NextResetTime = weeklyResetDt,
                    DetailType = ProviderUsageDetailType.QuotaWindow,
                    QuotaBucketKind = WindowKind.Rolling,
                    PercentageValue = weeklyUsedPct,
                    PercentageSemantic = PercentageValueSemantic.Used,
                    PercentageDecimalPlaces = 1,
                });
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
                        itemResetDt = dt.ToLocalTime();
                        var diff = itemResetDt.Value - DateTime.Now;
                        if (diff.TotalSeconds > 0 && diff < minDiff)
                        {
                            minDiff = diff;
                            soonestResetDt = itemResetDt;
                            soonestResetStr = $" (Resets: {resetDisplay})";
                        }
                    }

                    var quotaBucketKind = DetermineWindowKind(win.Duration, win.TimeUnit);

                    details.Add(new ProviderUsageDetail
                    {
                        Name = name,
                        Description = $"{det.Remaining} / {det.Limit} remaining (Resets: {resetDisplay})",
                        NextResetTime = itemResetDt,
                        DetailType = ProviderUsageDetailType.QuotaWindow,
                        QuotaBucketKind = quotaBucketKind,
                        PercentageValue = itemUsedPercentage,
                        PercentageSemantic = PercentageValueSemantic.Used,
                        PercentageDecimalPlaces = 1,
                    });
                }
            }

            if (!string.IsNullOrEmpty(soonestResetStr))
            {
                description += soonestResetStr;
            }

            return new[]
            {
                new ProviderUsage
            {
                ProviderId = this.ProviderId,
                ProviderName = this.Definition.DisplayName,
                RequestsPercentage = remainingPercentage,
                RequestsUsed = used,
                RequestsAvailable = limit,
                UsageUnit = "Points",
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                IsAvailable = true,
                Description = description,
                RawJson = content,
                HttpStatus = (int)response.StatusCode,
                Details = details,
                NextResetTime = soonestResetDt,
                AuthSource = config.AuthSource,
            },
            };
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Kimi check failed");
            return new[] { this.CreateUnavailableUsageFromException(ex, authSource: config.AuthSource) };
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
