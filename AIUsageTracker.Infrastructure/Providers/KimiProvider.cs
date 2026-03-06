using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Providers;
using AIUsageTracker.Infrastructure.Http;

namespace AIUsageTracker.Infrastructure.Providers
{
    public class KimiProvider : ProviderBase
    {
        private const string ProviderIdValue = "kimi";
        private const string ProviderNameValue = "Kimi";
        private const string BaseUrl = "https://kimi.moonshot.cn/api/user/usage";

        public KimiProvider(IHttpClientFactory httpClientFactory, ILogger<KimiProvider> logger) 
            : base(httpClientFactory, logger)
        {
        }

        public override string ProviderId => ProviderIdValue;
        public override string ProviderName => ProviderNameValue;

        public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(string apiKey)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl);
            request.Headers.Add("Authorization", $"Bearer {apiKey}");

            var response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch Kimi usage: {StatusCode}", response.StatusCode);
                return new[] { ProviderUsage.Unavailable(ProviderIdValue, ProviderNameValue, "API request failed") };
            }

            var data = await response.Content.ReadFromJsonAsync<KimiUsageResponse>();
            if (data?.Usage == null)
            {
                return new[] { ProviderUsage.Unavailable(ProviderIdValue, ProviderNameValue, "Invalid response format") };
            }

            var details = new List<ProviderUsageDetail>();
            var used = data.Usage.Used;
            var limit = data.Usage.Limit;
            var remaining = data.Usage.Remaining;

            var soonestReset = DateTime.MaxValue;
            var description = "Active";

            // Add weekly limit from usage as Secondary detail (always, as this is the primary quota)
            if (limit > 0 && remaining >= 0)
            {
                var weeklyUsedPct = ((limit - remaining) / (double)limit) * 100.0;
                DateTime? weeklyResetDt = null;
                if (!string.IsNullOrEmpty(data.Usage.ResetTime) && 
                    DateTime.TryParse(data.Usage.ResetTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var weeklyDt))
                {
                    weeklyResetDt = weeklyDt.ToLocalTime();
                    if (weeklyResetDt < soonestReset) soonestReset = weeklyResetDt.Value;
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

            // Add tiered model quotas if available
            if (data.Usage.ModelQuotas?.Any() == true)
            {
                foreach (var quota in data.Usage.ModelQuotas)
                {
                    var modelUsed = quota.Limit - quota.Remaining;
                    var modelUsedPct = quota.Limit > 0 ? (modelUsed / (double)quota.Limit) * 100.0 : 0;
                    
                    DateTime? modelResetDt = null;
                    if (!string.IsNullOrEmpty(quota.ResetTime) && 
                        DateTime.TryParse(quota.ResetTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var mDt))
                    {
                        modelResetDt = mDt.ToLocalTime();
                        if (modelResetDt < soonestReset) soonestReset = modelResetDt.Value;
                    }

                    // Map specific windows to kinds
                    var windowKind = WindowKind.None;
                    if (quota.Name.Contains("3h", StringComparison.OrdinalIgnoreCase)) windowKind = WindowKind.Primary;
                    else if (quota.Name.Contains("Spark", StringComparison.OrdinalIgnoreCase)) windowKind = WindowKind.Spark;

                    details.Add(new ProviderUsageDetail
                    {
                         Name = quota.Name,
                         Used = $"{modelUsedPct.ToString("F1", CultureInfo.InvariantCulture)}% used",
                         Description = $"{quota.Remaining} remaining",
                         NextResetTime = modelResetDt,
                         DetailType = ProviderUsageDetailType.QuotaWindow,
                         WindowKind = windowKind
                    });
                }
            }

            if (soonestReset != DateTime.MaxValue)
            {
                description += $" (Resets in {GetRelativeTime(soonestReset)})";
            }

            return new[] { new ProviderUsage
            {
                ProviderId = ProviderIdValue,
                ProviderName = ProviderNameValue,
                IsAvailable = true,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                RequestsPercentage = limit > 0 ? (remaining / (double)limit) * 100.0 : 100.0, // Remaining %
                RequestsUsed = used,
                RequestsAvailable = limit,
                UsageUnit = "Requests",
                Description = description,
                NextResetTime = soonestReset == DateTime.MaxValue ? null : soonestReset,
                Details = details,
                AuthSource = "API Key"
            }};
        }

        private static string FormatResetTime(string? isoTime)
        {
            if (string.IsNullOrEmpty(isoTime) || !DateTime.TryParse(isoTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return isoTime ?? "unknown";
            return dt.ToLocalTime().ToString("MMM d, HH:mm");
        }

        private static string GetRelativeTime(DateTime resetTime)
        {
            var diff = resetTime - DateTime.Now;
            if (diff.TotalDays >= 1) return $"{(int)diff.TotalDays}d";
            if (diff.TotalHours >= 1) return $"{(int)diff.TotalHours}h";
            return $"{(int)Math.Ceiling(diff.TotalMinutes)}m";
        }

        private class KimiUsageResponse
        {
            [JsonPropertyName("usage")]
            public KimiUsageInfo? Usage { get; set; }
        }

        private class KimiUsageInfo
        {
            [JsonPropertyName("used")]
            public long Used { get; set; }
            
            [JsonPropertyName("limit")]
            public long Limit { get; set; }
            
            [JsonPropertyName("remaining")]
            public long Remaining { get; set; }
            
            [JsonPropertyName("resetTime")]
            public string? ResetTime { get; set; }

            [JsonPropertyName("modelQuotas")]
            public List<KimiModelQuota>? ModelQuotas { get; set; }
        }

        private class KimiModelQuota
        {
             [JsonPropertyName("name")]
             public string Name { get; set; } = string.Empty;
             
             [JsonPropertyName("limit")]
             public long Limit { get; set; }
             
             [JsonPropertyName("remaining")]
             public long Remaining { get; set; }
             
             [JsonPropertyName("resetTime")]
             public string? ResetTime { get; set; }
        }
    }
}
