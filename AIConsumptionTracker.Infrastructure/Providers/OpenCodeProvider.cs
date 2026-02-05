using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;

namespace AIConsumptionTracker.Infrastructure.Providers;

public class OpenCodeProvider : IProviderService
{
    public string ProviderId => "opencode";
    private readonly HttpClient _httpClient;

    public OpenCodeProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            throw new ArgumentException("API Key not found for OpenCode provider.");
        }

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.opencode.ai/v1/credits");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);

        var response = await _httpClient.SendAsync(request);
        
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"OpenCode API returned {response.StatusCode}");
        }

        var content = await response.Content.ReadAsStringAsync();

        if (content.Trim() == "Not Found")
        {
             // API endpoint exists but returns "Not Found" text for some keys/situations
             // Return a safe "Not Available" state instead of crashing
             return new[] { new ProviderUsage
             {
                 ProviderId = ProviderId,
                 ProviderName = "OpenCode",
                 UsagePercentage = 0,
                 CostUsed = 0,
                 CostLimit = 0,
                 UsageUnit = "Credits",
                 IsQuotaBased = false,
                 IsAvailable = false,
                 Description = "Service Unavailable"
             }};
        }

        OpenCodeCreditsResponse? data;
        try 
        {
            data = System.Text.Json.JsonSerializer.Deserialize<OpenCodeCreditsResponse>(content);
        }
        catch (System.Text.Json.JsonException)
        {
             throw new Exception($"Failed to parse OpenCode response: {content}");
        }
        
        if (data?.Data == null)
        {
            throw new Exception("Failed to parse OpenCode credits response structure.");
        }

        var total = data.Data.TotalCredits;
        var used = data.Data.UsedCredits;
        var utilization = total > 0 ? (used / total) * 100.0 : 0;

        return new[] { new ProviderUsage
        {
            ProviderId = ProviderId,
            ProviderName = "OpenCode",
            UsagePercentage = Math.Min(utilization, 100),
            CostUsed = used,
            CostLimit = total,
            UsageUnit = "Credits",
            IsQuotaBased = false, // Pay as you go nominally, but acts like credit consumption
            PaymentType = PaymentType.Credits,
            Description = $"{used:F2} / {total:F2} credits"

        }};
    }

    private class OpenCodeCreditsResponse
    {
        [JsonPropertyName("data")]
        public CreditsData? Data { get; set; }
    }

    private class CreditsData
    {
        [JsonPropertyName("total_credits")]
        public double TotalCredits { get; set; }

        [JsonPropertyName("used_credits")]
        public double UsedCredits { get; set; }

        [JsonPropertyName("remaining_credits")]
        public double RemainingCredits { get; set; }
    }
}

