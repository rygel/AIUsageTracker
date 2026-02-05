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

    public async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config)
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

        double usedPercent = 0;
        string detailInfo = "";

        if (tokenLimit != null)
        {
            planDescription = "Coding Plan";
            double limitPercent = tokenLimit.Percentage ?? 
                (tokenLimit.CurrentValue.HasValue && tokenLimit.Total.HasValue && tokenLimit.Total.Value > 0 
                ? (double)tokenLimit.CurrentValue.Value / tokenLimit.Total.Value * 100.0 
                : 0);
            usedPercent = Math.Max(usedPercent, limitPercent);
            
            if (tokenLimit.Total > 50000000) {
                 planDescription = "Coding Plan (Ultra/Enterprise)";
            } else if (tokenLimit.Total > 10000000) {
                 planDescription = "Coding Plan (Pro)";
            }
            
            detailInfo = $"{usedPercent:F1}% of {tokenLimit.Total / 1000000.0:F0}M tokens used";
        }
        
        if (mcpLimit != null && mcpLimit.Percentage > 0)
        {
            usedPercent = Math.Max(usedPercent, mcpLimit.Percentage.Value);
        }

        // Z.AI usually resets at UTC midnight
        var resetDt = DateTime.UtcNow.Date.AddDays(1);
        var rDiff = resetDt.ToLocalTime() - DateTime.Now;
        string zReset = $" (Resets: ({resetDt.ToLocalTime():MMM dd HH:mm}))";

        return new[] { new ProviderUsage
        {
            ProviderId = ProviderId,
            ProviderName = $"Z.AI {planDescription}",
            UsagePercentage = Math.Min(usedPercent, 100),
            CostUsed = usedPercent,
            CostLimit = 100,
            UsageUnit = "Quota %",
            IsQuotaBased = true, 
            PaymentType = PaymentType.Quota,
            Description = (string.IsNullOrEmpty(detailInfo) ? $"{usedPercent:F1}% utilized" : detailInfo) + zReset,

            NextResetTime = resetDt.ToLocalTime()
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
    }
}

