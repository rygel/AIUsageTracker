using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;

namespace AIConsumptionTracker.Infrastructure.Providers;

public class ZaiProvider : IProviderService
{
    public string ProviderId => "zai-coding-plan";
    private readonly HttpClient _httpClient;
    private readonly ILogger<ZaiProvider> _logger;

    public ZaiProvider(HttpClient httpClient, ILogger<ZaiProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            throw new ArgumentException("API Key not found for Z.AI provider.");
        }

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.z.ai/api/monitor/usage/quota/limit");
        
        // Z.AI uses raw key in Authorization header without "Bearer" prefix based on Swift ref
        request.Headers.TryAddWithoutValidation("Authorization", config.ApiKey);
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en");

        var response = await _httpClient.SendAsync(request);
        
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"Z.AI API returned {response.StatusCode}");
        }

        // Response is wrapped in a "data" envelope sometimes, or direct?
        // Swift code handles both Envelope<T> and direct T. 
        // Let's assume Envelope based on "ZaiEnvelope" struct.
        var responseString = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"[DEBUG] ZAI DATA: {responseString}");
        var envelope = JsonSerializer.Deserialize<ZaiEnvelope<ZaiQuotaLimitResponse>>(responseString);
        
        string planDescription = "API"; 
        
        var limits = envelope?.Data?.Limits;
        if (limits == null || !limits.Any())
        {
             // If no limits found, maybe it's just a simple API key with no usage info available yet
             throw new Exception("No usage limits found for this Z.AI key.");
        }

        // Look for indicators of a Coding Plan (subscription)
        var tokenLimit = limits.FirstOrDefault(l => l.Type?.ToUpper() == "TOKENS_LIMIT");
        var mcpLimit = limits.FirstOrDefault(l => l.Type?.ToUpper() == "TIME_LIMIT");

            double remainingPercent = 100;
        string detailInfo = "";

        if (tokenLimit != null)
        {
            planDescription = "Coding Plan";
            // Map JSON properties more clearly:
            // tokenLimit.CurrentValue likely means USED amount in this API if 0 = unused.
            // tokenLimit.Total (mapped from 'usage') is the LIMIT.
            // For quota-based providers, show REMAINING percentage (full bar = lots remaining)
            double usedVal = tokenLimit.CurrentValue ?? 0;
            double totalVal = tokenLimit.Total ?? 0;
            double remainingPercentVal = (totalVal > 0 
                ? ((totalVal - usedVal) / totalVal) * 100.0 
                : 100);
            
            remainingPercent = Math.Min(remainingPercent, remainingPercentVal);
            
            if (tokenLimit.Total > 50000000) {
                 planDescription = "Coding Plan (Ultra/Enterprise)";
            } else if (tokenLimit.Total > 10000000) {
                 planDescription = "Coding Plan (Pro)";
            }
            
            double usedPercentDisplay = totalVal > 0 ? (usedVal / totalVal) * 100.0 : 0;
            detailInfo = $"{usedPercentDisplay:F1}% Used of {tokenLimit.Total / 1000000.0:F0}M tokens limit";
        }
        
        if (mcpLimit != null && mcpLimit.Percentage > 0)
        {
            // MCP limit percentage is "used", convert to "remaining" for consistency
            double mcpRemainingPercent = Math.Max(0, 100 - mcpLimit.Percentage.Value);
            remainingPercent = Math.Min(remainingPercent, mcpRemainingPercent);
        }

        // Get next reset time from the first limit that has it
        DateTime? nextResetTime = null;
        var limitWithReset = limits.FirstOrDefault(l => !string.IsNullOrEmpty(l.NextResetTime));
        if (limitWithReset != null && DateTime.TryParse(limitWithReset.NextResetTime, out var resetDt))
        {
            nextResetTime = resetDt.ToLocalTime();
        }

        return new[] { new ProviderUsage
        {
            ProviderId = ProviderId,
            ProviderName = $"Z.AI {planDescription}",
            UsagePercentage = Math.Min(remainingPercent, 100),
            CostUsed = 100 - remainingPercent,  // Store actual used percentage
            CostLimit = 100,
            UsageUnit = "Quota %",
            IsQuotaBased = true, 
            PaymentType = PaymentType.Quota,
            Description = string.IsNullOrEmpty(detailInfo) ? $"{100 - remainingPercent:F1}% utilized" : detailInfo,
            NextResetTime = nextResetTime
        }};
    }

    private class ZaiEnvelope<T>
    {
        [JsonPropertyName("data")]
        public T? Data { get; set; }
    }

    private class ZaiQuotaLimitResponse
    {
        [JsonPropertyName("limits")]
        public List<ZaiQuotaLimitItem>? Limits { get; set; }
    }

    private class ZaiQuotaLimitItem
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
        public string? NextResetTime { get; set; }
    }
}

