using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;

namespace AIConsumptionTracker.Infrastructure.Providers;

public class OpenRouterProvider : IProviderService
{
    public string ProviderId => "openrouter";
    private readonly HttpClient _httpClient;

    public OpenRouterProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ProviderUsage> GetUsageAsync(ProviderConfig config)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            // If API key is missing, we can't fetch.
            // However, this might be a placeholder config?
            throw new ArgumentException("API Key not found for OpenRouter provider.");
        }

        var request = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/credits");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);

        var response = await _httpClient.SendAsync(request);
        
        if (!response.IsSuccessStatusCode)
        {
             // Try fetching "key" endpoint if "credits" fails? 
             // Ref code fetches both credits and key info. Simplest is start with credits.
            throw new Exception($"OpenRouter API returned {response.StatusCode}");
        }

        var data = await response.Content.ReadFromJsonAsync<OpenRouterCreditsResponse>();
        
        if (data?.Data == null)
        {
            throw new Exception("Failed to parse OpenRouter credits response.");
        }


        var details = new List<ProviderUsageDetail>();
        string label = "OpenRouter";
        
        try 
        {
            var keyRequest = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/key");
            keyRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);
            var keyResponse = await _httpClient.SendAsync(keyRequest);
            if (keyResponse.IsSuccessStatusCode)
            {
                var keyData = await keyResponse.Content.ReadFromJsonAsync<OpenRouterKeyResponse>();
                if (keyData?.Data != null)
                {
                    label = keyData.Data.Label ?? "OpenRouter";
                    if (keyData.Data.Limit > 0)
                    {
                        string resetStr = "";
                        // If LimitReset is available (it's a string like "2023-10-01T...")
                        /* 
                           Note: The OpenRouter key API might return limit_remaining or other fields. 
                           The research showed limit_reset in `query-openrouter.sh` key endpoint response structure?
                           Actually query-openrouter.sh showed: 
                           limit_reset: .data.limit_reset
                        */
                        DateTime? nextResetTime = null;
                        if (!string.IsNullOrEmpty(keyData.Data.LimitReset))
                        {
                            if (DateTime.TryParse(keyData.Data.LimitReset, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt))
                            {
                                 var diff = dt.ToLocalTime() - DateTime.Now;
                                 if (diff.TotalSeconds > 0)
                                 {
                                     resetStr = $" (Resets: ({dt.ToLocalTime():MMM dd HH:mm}))";
                                     nextResetTime = dt.ToLocalTime();
                                 }
                            }
                        }

                        details.Add(new ProviderUsageDetail 
                        { 
                            Name = "Spending Limit", 
                            Description = $"{keyData.Data.Limit:F2}{resetStr}", 
                            Used = "",
                            NextResetTime = nextResetTime
                        });
                    }
                    
                    details.Add(new ProviderUsageDetail { Name = "Free Tier", Description = keyData.Data.IsFreeTier ? "Yes" : "No", Used = "" });
                }
            }
        }
        catch { /* Ignore key fetch errors, credits are main */ }

        var total = data.Data.TotalCredits;
        var used = data.Data.TotalUsage;
        var utilization = total > 0 ? (used / total) * 100.0 : 0;
        var remaining = total - used;
        
        string mainReset = "";
        var spendingLimitDetail = details.FirstOrDefault(d => d.Name == "Spending Limit");
        if (spendingLimitDetail != null && spendingLimitDetail.Description.Contains("(Resets:"))
        {
            var idx = spendingLimitDetail.Description.IndexOf("(Resets:");
            if (idx >= 0) mainReset = " " + spendingLimitDetail.Description.Substring(idx);
        }

        return new ProviderUsage
        {
            ProviderId = config.ProviderId, // Keep original ID (likely "openrouter")
            ProviderName = label, // Use label from key
            UsagePercentage = Math.Min(utilization, 100),
            CostUsed = used,
            CostLimit = total,
            PaymentType = PaymentType.Credits,
            UsageUnit = "Credits",
            IsQuotaBased = true,

            IsAvailable = true,
            Description = $"{remaining:F2} Credits Remaining{mainReset}",
            NextResetTime = spendingLimitDetail?.NextResetTime,
            Details = details
        };
    }

    private class OpenRouterCreditsResponse
    {
        [JsonPropertyName("data")]
        public CreditsData? Data { get; set; }
    }

    private class CreditsData
    {
        [JsonPropertyName("total_credits")]
        public double TotalCredits { get; set; }

        [JsonPropertyName("total_usage")]
        public double TotalUsage { get; set; }
    }

    private class OpenRouterKeyResponse
    {
        [JsonPropertyName("data")]
        public KeyData? Data { get; set; }
    }

    private class KeyData
    {
        [JsonPropertyName("label")]
        public string? Label { get; set; }

        [JsonPropertyName("limit")]
        public double Limit { get; set; }

        [JsonPropertyName("limit_reset")]
        public string? LimitReset { get; set; }
        
        [JsonPropertyName("is_free_tier")]
        public bool IsFreeTier { get; set; }
    }
}

