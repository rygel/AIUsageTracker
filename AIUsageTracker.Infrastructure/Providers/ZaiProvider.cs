using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Infrastructure.Providers;

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
                 PlanType = PlanType.Coding
             }};
        }

        // Log all limits for debugging
        foreach (var limit in limits)
        {
            _logger.LogDebug("[ZAI LIMIT] Type={Type} Total={Total} CurrentValue={Current} Remaining={Remaining} Percentage={Pct} ResetTime={Ts}",
                limit.Type, limit.Total, limit.CurrentValue, limit.Remaining, limit.Percentage, limit.NextResetTime);
        }

        // Helper to check if a limit is in the future (or has no expiry)
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        
        bool IsFuture(long? resetTime) 
        {
            if (!resetTime.HasValue || resetTime.Value == 0) return true; // No reset time = always active
            var ts = resetTime.Value;
            // Heuristic to detect milliseconds vs seconds (similar to logic below)
            if (ts > 10000000000) return ts > nowMs; 
            return ts > nowSec;
        }

        var tokenLimits = limits.Where(l =>
            l.Type != null && (l.Type.Equals("TOKENS_LIMIT", StringComparison.OrdinalIgnoreCase) ||
                               l.Type.Equals("Tokens", StringComparison.OrdinalIgnoreCase))).ToList();
                               
        // Prefer active limits (future reset time or no reset time)
        // If multiple active limits exist, pick the specific one that is most restrictive (lowest remaining)
        // If no active limits, fall back to any limit (historical)
        var tokenLimit = tokenLimits.Where(l => IsFuture(l.NextResetTime))
                                    .OrderBy(l => l.Remaining ?? long.MaxValue)
                                    .FirstOrDefault() 
                         ?? tokenLimits.FirstOrDefault();

        var mcpLimit = limits.FirstOrDefault(l =>
            l.Type != null && (l.Type.Equals("TIME_LIMIT", StringComparison.OrdinalIgnoreCase) ||
                               l.Type.Equals("Time", StringComparison.OrdinalIgnoreCase)));

        _logger.LogDebug("[ZAI] Found token limit: {Found}, mcp limit: {McpFound}",
            tokenLimit != null, mcpLimit != null);

        double? remainingPercent = null;
        string detailInfo = "";
        
        // Define variables to hold real values if available, otherwise default to percentage logic
        double finalRequestsAvailable = 100;
        double finalRequestsUsedReal = 0;
        var hasRawLimitData = false;

        if (tokenLimit != null)
        {
            planDescription = "Coding Plan";
            _logger.LogDebug("[ZAI] Processing TOKENS_LIMIT - Percentage={Pct} Total={Total} Current={Current} Remaining={Remaining}",
                tokenLimit.Percentage, tokenLimit.Total, tokenLimit.CurrentValue, tokenLimit.Remaining);

            if (tokenLimit.Percentage.HasValue && tokenLimit.Total == null && tokenLimit.CurrentValue == null && tokenLimit.Remaining == null)
            {
                double usedPercent = tokenLimit.Percentage.Value;
                double remainingPercentVal = 100 - usedPercent;
                remainingPercent = remainingPercent.HasValue
                    ? Math.Min(remainingPercent.Value, remainingPercentVal)
                    : remainingPercentVal;
                detailInfo = $"{remainingPercentVal.ToString("F1", CultureInfo.InvariantCulture)}% Remaining";
                _logger.LogDebug("[ZAI] Percentage-only mode: {Used}% used, {Remaining}% remaining", usedPercent, remainingPercentVal);
                
                finalRequestsUsedReal = 100 - remainingPercentVal;
            }
            else
            {
                double totalVal = tokenLimit.Total ?? 0;
                double usedVal = tokenLimit.CurrentValue ?? 0;
                double remainingVal = tokenLimit.Remaining ?? (totalVal - usedVal);

                if (tokenLimit.Total > 50000000) {
                     planDescription = "Coding Plan (Ultra/Enterprise)";
                } else if (tokenLimit.Total > 10000000) {
                     planDescription = "Coding Plan (Pro)";
                }

                if (totalVal > 0)
                {
                    double remainingPercentVal = (remainingVal / totalVal) * 100.0;

                    _logger.LogDebug("[ZAI] Calculation: Remaining={RemainingVal} / Total={TotalVal} = {Percent}%",
                        remainingVal, totalVal, remainingPercentVal);

                    remainingPercent = remainingPercent.HasValue
                        ? Math.Min(remainingPercent.Value, remainingPercentVal)
                        : remainingPercentVal;

                    // Expose raw values for UI to display "X / Y used"
                    finalRequestsAvailable = totalVal;
                    finalRequestsUsedReal = usedVal;
                    hasRawLimitData = true;
                    detailInfo = $"{remainingPercentVal.ToString("F1", CultureInfo.InvariantCulture)}% Remaining of {(totalVal / 1000000.0).ToString("F0", CultureInfo.InvariantCulture)}M tokens limit";
                }
                else if (tokenLimit.Percentage.HasValue)
                {
                    double usedPercent = tokenLimit.Percentage.Value;
                    double remainingPercentVal = 100 - usedPercent;
                    remainingPercent = remainingPercent.HasValue
                        ? Math.Min(remainingPercent.Value, remainingPercentVal)
                        : remainingPercentVal;
                    detailInfo = $"{remainingPercentVal.ToString("F1", CultureInfo.InvariantCulture)}% Remaining";
                    finalRequestsUsedReal = 100 - remainingPercentVal;
                }
                else
                {
                    _logger.LogDebug("[ZAI] Token limit missing usable quota metrics; usage unknown");
                }
            }
        }

        if (mcpLimit != null && mcpLimit.Percentage > 0)
        {
            _logger.LogDebug("[ZAI] Processing TIME_LIMIT - Percentage: {Percentage}", mcpLimit.Percentage);
            double mcpRemainingPercent = Math.Max(0, 100 - mcpLimit.Percentage.Value);
            remainingPercent = remainingPercent.HasValue
                ? Math.Min(remainingPercent.Value, mcpRemainingPercent)
                : mcpRemainingPercent;
            _logger.LogDebug("[ZAI] MCP remaining percent: {McpRemainingPercent}", mcpRemainingPercent);
        }

        DateTime? nextResetTime = null;
        string resetStr = "";
        var limitWithReset = limits
            .Where(l => l.NextResetTime.HasValue && l.NextResetTime.Value > 0)
            .OrderBy(l => l.NextResetTime!.Value)
            .FirstOrDefault();
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

        if (!remainingPercent.HasValue)
        {
            return new[] { new ProviderUsage
            {
                ProviderId = ProviderId,
                ProviderName = $"Z.AI {planDescription}",
                IsAvailable = false,
                Description = "Usage unknown (missing quota metrics)",
                IsQuotaBased = true,
                PlanType = PlanType.Coding
            }};
        }

        var finalRequestsPercentage = Math.Min(remainingPercent.Value, 100);

        if (!hasRawLimitData)
        {
            finalRequestsAvailable = 100;
            finalRequestsUsedReal = Math.Max(0, 100 - finalRequestsPercentage);
        }

        var finalDescription = (string.IsNullOrEmpty(detailInfo) ? $"{finalRequestsPercentage.ToString("F1", CultureInfo.InvariantCulture)}% remaining" : detailInfo) + resetStr;

        _logger.LogInformation("Z.AI Provider Usage - ProviderId: {ProviderId}, ProviderName: {ProviderName}, RequestsPercentage: {RequestsPercentage}%, RequestsUsed: {RequestsUsed}%, Description: {Description}, IsAvailable: {IsAvailable}",
            ProviderId, $"Z.AI {planDescription}", finalRequestsPercentage, finalRequestsUsedReal, finalDescription, true);
        
        return new[] { new ProviderUsage
        {
            ProviderId = ProviderId,
            ProviderName = $"Z.AI {planDescription}",
            RequestsPercentage = finalRequestsPercentage,
            RequestsUsed = finalRequestsUsedReal,  // Store actual used count/percentage
            RequestsAvailable = finalRequestsAvailable, // Store actual total limit
            UsageUnit = finalRequestsAvailable > 100 ? "Tokens" : "Quota %",
            IsQuotaBased = true, 
            PlanType = PlanType.Coding,
            DisplayAsFraction = finalRequestsAvailable > 100, // Explicitly request fraction display if we have real numbers
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

