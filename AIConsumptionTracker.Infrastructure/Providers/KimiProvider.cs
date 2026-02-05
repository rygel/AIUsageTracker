using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;

namespace AIConsumptionTracker.Infrastructure.Providers;

public class KimiProvider : IProviderService
{
    public string ProviderId => "kimi";
    private readonly HttpClient _httpClient;
    private readonly ILogger<KimiProvider> _logger;

    public KimiProvider(HttpClient httpClient, ILogger<KimiProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ProviderUsage> GetUsageAsync(ProviderConfig config)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            return new ProviderUsage
            {
                ProviderId = config.ProviderId,
                ProviderName = "Kimi",
                IsAvailable = false,
                Description = "API Key missing"
            };
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.kimi.com/coding/v1/usages");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var data = await response.Content.ReadFromJsonAsync<KimiUsageResponse>();
            if (data == null || data.Usage == null) throw new Exception("Invalid response from Kimi API");

            double used = data.Usage.Used;
            double limit = data.Usage.Limit;
            double remaining = data.Usage.Remaining;
            
            double usedPercentage = 0;
            if (limit > 0)
            {
                usedPercentage = (used / limit) * 100.0;
            }
            
            // Correction: current API might return used/remaining/limit logic differently
            // Based on script: USAGE_PCT_LEFT = REMAINING * 100 / LIMIT
            // So used% = 100 - (REMAINING * 100 / LIMIT)
            if (limit > 0)
            {
                usedPercentage = 100.0 - ((remaining / limit) * 100.0);
            }

            var description = $"{usedPercentage:F1}% Used ({remaining}/{limit})";
            if (limit == 0) description = "Unlimited / Pay-as-you-go";
            
            // Limits Detail
            string soonestResetStr = "";
            DateTime? soonestResetDt = null;
            var details = new List<ProviderUsageDetail>();
            TimeSpan minDiff = TimeSpan.MaxValue;

            if (data.Limits != null)
            {
                foreach (var limitItem in data.Limits)
                {
                    if (limitItem.Detail == null || limitItem.Window == null) continue;
                    
                    var win = limitItem.Window;
                    var det = limitItem.Detail;
                    
                    if (det.Limit <= 0) continue;

                    string name = $"{FormatDuration(win.Duration, win.TimeUnit ?? "TIME_UNIT_MINUTE")} Limit";
                    double itemPct = 100.0 - ((det.Remaining / (double)det.Limit) * 100.0);
                    
                    var resetDisplay = FormatResetTime(det.ResetTime ?? "");
                    DateTime? itemResetDt = null;
                    if (DateTime.TryParse(det.ResetTime, out var dt))
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

                    details.Add(new ProviderUsageDetail
                    {
                         Name = name,
                         Used = $"{itemPct:F1}%",
                         Description = $"{det.Remaining} remaining (Resets: {resetDisplay})", // Kept original description for detail item
                         NextResetTime = itemResetDt
                    });
                }
            }
            
            if (!string.IsNullOrEmpty(soonestResetStr)) description += soonestResetStr; // Used soonestResetStr

            return new ProviderUsage
            {
                ProviderId = config.ProviderId,
                ProviderName = "Kimi",
                UsagePercentage = usedPercentage,
                CostUsed = used,
                CostLimit = limit,
                UsageUnit = "Points", 
                IsQuotaBased = true,
                PaymentType = PaymentType.Quota,
                IsAvailable = true,
                Description = description,

                Details = details,
                NextResetTime = soonestResetDt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Kimi usage");
            return new ProviderUsage
            {
                ProviderId = config.ProviderId,
                ProviderName = "Kimi",
                IsAvailable = false,
                Description = $"Error: {ex.Message}"
            };
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
        if (DateTime.TryParse(resetTime, out var dt))
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
