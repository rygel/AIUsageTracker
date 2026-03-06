using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;

namespace AIUsageTracker.Infrastructure.Providers;

public class KimiProvider : ProviderBase
{
    public static ProviderDefinition StaticDefinition { get; } = new(
        providerId: "kimi",
        displayName: "Kimi",
        planType: PlanType.Coding,
        isQuotaBased: true,
        defaultConfigType: "quota-based",
        includeInWellKnownProviders: true);

    public override ProviderDefinition Definition => StaticDefinition;
    public override string ProviderId => StaticDefinition.ProviderId;
    private readonly HttpClient _httpClient;
    private readonly ILogger<KimiProvider> _logger;

    public KimiProvider(HttpClient httpClient, ILogger<KimiProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            return new[] { CreateUnavailableUsage("API Key missing", authSource: config.AuthSource) };
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.kimi.com/coding/v1/usages");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch Kimi usage: {StatusCode}", response.StatusCode);
                return new[] { CreateUnavailableUsageFromStatus(response, authSource: config.AuthSource) };
            }

            var content = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<KimiUsageResponse>(content);
            if (data == null || data.Usage == null)
            {
                return new[] { CreateUnavailableUsage("Invalid response format", authSource: config.AuthSource) };
            }

            double used = data.Usage.Used;
            double limit = data.Usage.Limit;
            double remaining = data.Usage.Remaining;

            var remainingPercentage = limit > 0
                ? UsageMath.CalculateRemainingPercent(used, limit)
                : 100.0;

            var description = "Active";

            // Limits Detail
            string soonestResetStr = "";
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
                    Used = $"{weeklyUsedPct.ToString("F1", CultureInfo.InvariantCulture)}% used",
                    Description = $"{remaining} remaining{(!string.IsNullOrEmpty(data.Usage.ResetTime) ? $" (Resets: {FormatResetTime(data.Usage.ResetTime)})" : "")}",
                    NextResetTime = weeklyResetDt,
                    DetailType = ProviderUsageDetailType.QuotaWindow,
                    WindowKind = WindowKind.Secondary
                });
            }

            if (data.Limits != null)
            {
                foreach (var limitItem in data.Limits)
                {
                    if (limitItem.Detail == null || limitItem.Window == null) continue;

                    var win = limitItem.Window;
                    var det = limitItem.Detail;

                    if (det.Limit <= 0) continue;

                    string name = $"{FormatDuration(win.Duration, win.TimeUnit ?? "TIME_UNIT_MINUTE")} Limit";
                    var itemUsed = det.Limit - det.Remaining;
                    var itemUsedPercentage = det.Limit > 0 ? (itemUsed / (double)det.Limit) * 100.0 : 0;

                    var resetDisplay = FormatResetTime(det.ResetTime ?? "");
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

                    var windowKind = DetermineWindowKind(win.Duration, win.TimeUnit);

                    details.Add(new ProviderUsageDetail
                    {
                         Name = name,
                         Used = $"{itemUsedPercentage.ToString("F1", CultureInfo.InvariantCulture)}% used",
                         Description = $"{det.Remaining} / {det.Limit} remaining (Resets: {resetDisplay})",
                         NextResetTime = itemResetDt,
                         DetailType = ProviderUsageDetailType.QuotaWindow,
                         WindowKind = windowKind
                    });
                }
            }

            if (!string.IsNullOrEmpty(soonestResetStr))
            {
                description += soonestResetStr;
            }

            return new[] { new ProviderUsage
            {
                ProviderId = ProviderId,
                ProviderName = Definition.DisplayName,
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
                AuthSource = config.AuthSource
            }};
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kimi check failed");
            return new[] { CreateUnavailableUsageFromException(ex, authSource: config.AuthSource) };
        }
    }

    private string FormatDuration(long duration, string unit)
    {
         if (unit == "TIME_UNIT_MINUTE") return duration == 60 ? "Hourly" : $"{duration}m";
         if (unit == "TIME_UNIT_HOUR") return $"{duration}h";
         if (unit == "TIME_UNIT_DAY") return $"{duration}d";
         return unit;
    }

    private string FormatResetTime(string resetTime)
    {
        if (DateTime.TryParse(resetTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return $"({dt:MMM dd HH:mm})";
        }
        return resetTime;
    }

    private static WindowKind DetermineWindowKind(long duration, string? unit)
    {
        if (unit == "TIME_UNIT_DAY" && duration >= 7)
        {
            return WindowKind.Secondary;
        }

        return WindowKind.Primary;
    }

    private class KimiUsageResponse
    {
        [JsonPropertyName("usage")]
        public KimiUsageData? Usage { get; set; }

        [JsonPropertyName("limits")]
        public List<KimiLimitItem>? Limits { get; set; }
    }

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
