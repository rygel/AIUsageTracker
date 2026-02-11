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

        _logger.LogDebug("[ZAI] Sending API request to https://api.z.ai/api/monitor/usage/quota/limit");
        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("[ZAI] API returned {StatusCode}", response.StatusCode);
            throw new Exception($"Z.AI API returned {response.StatusCode}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        _logger.LogDebug("[ZAI RAW RESPONSE] {Response}", responseString);

        // Parse envelope
        var envelope = JsonSerializer.Deserialize<ZaiEnvelope<ZaiQuotaLimitResponse>>(responseString);
        _logger.LogDebug("[ZAI] Parsed envelope - Data is null: {IsNull}", envelope?.Data == null);
        
        string planDescription = "API";

        var limits = envelope?.Data?.Limits;
        _logger.LogDebug("[ZAI] Limits count: {Count}", limits?.Count ?? 0);

        if (limits == null || !limits.Any())
        {
             _logger.LogDebug("[ZAI] No limits found in response");
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

        // Log all limits for debugging
        foreach (var limit in limits)
        {
            _logger.LogDebug("[ZAI LIMIT] Type={Type} Total={Total} CurrentValue={Current} Remaining={Remaining} Percentage={Pct} ResetTime={Ts}",
                limit.Type, limit.Total, limit.CurrentValue, limit.Remaining, limit.Percentage, limit.NextResetTime);
        }

        var tokenLimit = limits.FirstOrDefault(l =>
            l.Type != null && (l.Type.Equals("TOKENS_LIMIT", StringComparison.OrdinalIgnoreCase) ||
                               l.Type.Equals("Tokens", StringComparison.OrdinalIgnoreCase)));
        var mcpLimit = limits.FirstOrDefault(l =>
            l.Type != null && (l.Type.Equals("TIME_LIMIT", StringComparison.OrdinalIgnoreCase) ||
                               l.Type.Equals("Time", StringComparison.OrdinalIgnoreCase)));

        _logger.LogDebug("[ZAI] Found token limit: {Found}, mcp limit: {McpFound}",
            tokenLimit != null, mcpLimit != null);

        double remainingPercent = 100;
        string detailInfo = "";

        if (tokenLimit != null)
        {
            planDescription = "Coding Plan";
            _logger.LogDebug("[ZAI] Processing TOKENS_LIMIT - Percentage={Pct} Total={Total} Current={Current} Remaining={Remaining}",
                tokenLimit.Percentage, tokenLimit.Total, tokenLimit.CurrentValue, tokenLimit.Remaining);

            if (tokenLimit.Percentage.HasValue && tokenLimit.Total == null && tokenLimit.CurrentValue == null && tokenLimit.Remaining == null)
            {
                double usedPercent = tokenLimit.Percentage.Value;
                double remainingPercentVal = 100 - usedPercent;
                remainingPercent = Math.Min(remainingPercent, remainingPercentVal);
                detailInfo = $"{remainingPercentVal.ToString("F1", CultureInfo.InvariantCulture)}% Remaining";
                _logger.LogDebug("[ZAI] Percentage-only mode: {Used}% used, {Remaining}% remaining", usedPercent, remainingPercentVal);
            }
            else
            {
                double totalVal = tokenLimit.Total ?? 0;
                double usedVal = tokenLimit.CurrentValue ?? 0;
                double remainingVal = tokenLimit.Remaining ?? (totalVal - usedVal);

                double remainingPercentVal = (totalVal > 0
                    ? (remainingVal / totalVal) * 100.0
                    : 100);

                _logger.LogDebug("[ZAI] Calculation: Remaining={RemainingVal} / Total={TotalVal} = {Percent}%",
                    remainingVal, totalVal, remainingPercentVal);

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
            _logger.LogDebug("[ZAI] Processing TIME_LIMIT - Percentage: {Percentage}", mcpLimit.Percentage);
            double mcpRemainingPercent = Math.Max(0, 100 - mcpLimit.Percentage.Value);
            remainingPercent = Math.Min(remainingPercent, mcpRemainingPercent);
            _logger.LogDebug("[ZAI] MCP remaining percent: {McpRemainingPercent}", mcpRemainingPercent);
        }

        DateTime? nextResetTime = null;
        string resetStr = "";
        var limitWithReset = limits.FirstOrDefault(l => l.NextResetTime.HasValue && l.NextResetTime.Value > 0);
        if (limitWithReset != null)
        {
            var ts = limitWithReset.NextResetTime!.Value;
            _logger.LogDebug("[ZAI] Reset timestamp from API: {Ts}", ts);

            // Detect if seconds or milliseconds
            if (ts < 10000000000) // Likely seconds (e.g., 1773532800)
            {
                var utcTime = DateTimeOffset.FromUnixTimeSeconds(ts);
                nextResetTime = utcTime.LocalDateTime;
                _logger.LogDebug("[ZAI] Interpreted as SECONDS: {Utc} UTC -> {Local} Local",
                    utcTime.UtcDateTime, nextResetTime);
            }
            else // Likely milliseconds (e.g., 1773532800000)
            {
                var utcTime = DateTimeOffset.FromUnixTimeMilliseconds(ts);
                nextResetTime = utcTime.LocalDateTime;
                _logger.LogDebug("[ZAI] Interpreted as MILLISECONDS: {Utc} UTC -> {Local} Local",
                    utcTime.UtcDateTime, nextResetTime);
            }

            resetStr = $" (Resets: {nextResetTime:MMM dd, yyyy HH:mm} Local)";
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

