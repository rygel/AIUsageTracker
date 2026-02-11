using System.Globalization;
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
        _logger.LogDebug("[ZAI RAW API RESPONSE] {Response}", responseString);
        var envelope = JsonSerializer.Deserialize<ZaiEnvelope<ZaiQuotaLimitResponse>>(responseString);
        
        string planDescription = "API"; 
        
        var limits = envelope?.Data?.Limits;
        if (limits == null || !limits.Any())
        {
             // If no limits found, return unavailable status
             return new[] { new ProviderUsage
             {
                 ProviderId = ProviderId,
                 ProviderName = "Z.AI",
                 IsAvailable = false,
                 Description = "No usage data available",
                 IsQuotaBased = true,
                 PaymentType = PaymentType.Quota
             }};
        }

        // Look for indicators of a Coding Plan (subscription)
        // From DESIGN.md: 
        // - currentValue: Amount used
        // - remaining: Amount remaining
        // - usage: Total quota limit
        var tokenLimit = limits.FirstOrDefault(l => 
            l.Type != null && (l.Type.Equals("TOKENS_LIMIT", StringComparison.OrdinalIgnoreCase) || 
                               l.Type.Equals("Tokens", StringComparison.OrdinalIgnoreCase)));
        var mcpLimit = limits.FirstOrDefault(l => 
            l.Type != null && (l.Type.Equals("TIME_LIMIT", StringComparison.OrdinalIgnoreCase) || 
                               l.Type.Equals("Time", StringComparison.OrdinalIgnoreCase)));
        
        double remainingPercent = 100;
        string detailInfo = "";
        
        if (tokenLimit != null)
        {
            planDescription = "Coding Plan";
            
            // Check if API returns percentage-only response (after reset scenario)
            if (tokenLimit.Percentage.HasValue && tokenLimit.Total == null && tokenLimit.CurrentValue == null && tokenLimit.Remaining == null)
            {
                // API returns only percentage (e.g., percentage: 7 means 7% used)
                double usedPercent = tokenLimit.Percentage.Value;
                double remainingPercentVal = 100 - usedPercent;
                remainingPercent = Math.Min(remainingPercent, remainingPercentVal);
                detailInfo = $"{remainingPercentVal.ToString("F1", CultureInfo.InvariantCulture)}% Remaining";
            }
            else
            {
                // Map JSON properties (from DESIGN.md):
                // tokenLimit.Total (mapped from 'usage') is the LIMIT
                // tokenLimit.CurrentValue is the USED amount
                // tokenLimit.Remaining is the REMAINING amount
                // For quota-based providers, show REMAINING percentage (full bar = lots remaining)
                double totalVal = tokenLimit.Total ?? 0;
                double usedVal = tokenLimit.CurrentValue ?? 0;
                double remainingVal = tokenLimit.Remaining ?? (totalVal - usedVal);
                
                // Calculate remaining percentage per design: (remaining / total) * 100
                double remainingPercentVal = (totalVal > 0 
                    ? (remainingVal / totalVal) * 100.0 
                    : 100);
                
                remainingPercent = Math.Min(remainingPercent, remainingPercentVal);
                
                if (tokenLimit.Total > 50000000) {
                     planDescription = "Coding Plan (Ultra/Enterprise)";
                } else if (tokenLimit.Total > 10000000) {
                     planDescription = "Coding Plan (Pro)";
                }
                
                detailInfo = $"{remainingPercentVal.ToString("F1", CultureInfo.InvariantCulture)}% Remaining of {(totalVal / 1000000.0).ToString("F0", CultureInfo.InvariantCulture)}M tokens limit";
            }
        }
        
        if (mcpLimit != null && mcpLimit.Percentage > 0)
        {
            _logger.LogDebug("Processing MCP limit - Percentage: {Percentage}", mcpLimit.Percentage);
            // MCP limit percentage is "used", convert to "remaining" for consistency
            double mcpRemainingPercent = Math.Max(0, 100 - mcpLimit.Percentage.Value);
            remainingPercent = Math.Min(remainingPercent, mcpRemainingPercent);
            _logger.LogDebug("MCP remaining percent: {McpRemainingPercent}", mcpRemainingPercent);
        }

        // Get next reset time from the first limit that has it (Unix timestamp in milliseconds)
        DateTime? nextResetTime = null;
        string resetStr = "";
        var limitWithReset = limits.FirstOrDefault(l => l.NextResetTime.HasValue && l.NextResetTime.Value > 0);
        if (limitWithReset != null)
        {
            nextResetTime = DateTimeOffset.FromUnixTimeMilliseconds(limitWithReset.NextResetTime!.Value).LocalDateTime;
            resetStr = $" (Resets: {nextResetTime:MMM dd HH:mm})";
            _logger.LogDebug("Next reset time: {NextResetTime}", nextResetTime);
        }

        var finalUsagePercentage = Math.Min(remainingPercent, 100);
        var finalCostUsed = 100 - remainingPercent;
        var finalDescription = (string.IsNullOrEmpty(detailInfo) ? $"{remainingPercent.ToString("F1", CultureInfo.InvariantCulture)}% remaining" : detailInfo) + resetStr;
        
        _logger.LogInformation("Z.AI Provider Usage - ProviderId: {ProviderId}, ProviderName: {ProviderName}, UsagePercentage: {UsagePercentage}%, CostUsed: {CostUsed}%, Description: {Description}, IsAvailable: {IsAvailable}",
            ProviderId, $"Z.AI {planDescription}", finalUsagePercentage, finalCostUsed, finalDescription, true);
        
        return new[] { new ProviderUsage
        {
            ProviderId = ProviderId,
            ProviderName = $"Z.AI {planDescription}",
            UsagePercentage = finalUsagePercentage,
            CostUsed = finalCostUsed,  // Store actual used percentage
            CostLimit = 100,
            UsageUnit = "Quota %",
            IsQuotaBased = true, 
            PaymentType = PaymentType.Quota,
            Description = finalDescription,
            NextResetTime = nextResetTime,
            IsAvailable = true
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
        public long? NextResetTime { get; set; }
    }
}

