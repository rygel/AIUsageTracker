using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;

namespace AIConsumptionTracker.Infrastructure.Providers;

public class XiaomiProvider : IProviderService
{
    public string ProviderId => "xiaomi";
    private readonly HttpClient _httpClient;
    private readonly ILogger<XiaomiProvider> _logger;

    public XiaomiProvider(HttpClient httpClient, ILogger<XiaomiProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            return new[] { new ProviderUsage
            {
                ProviderId = config.ProviderId,
                ProviderName = "Xiaomi",
                IsAvailable = false,
                Description = "API Key missing"
            }};
        }

        try
        {
            // Endpoint based on research/best-guess
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.xiaomimimo.com/v1/user/balance");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<XiaomiResponse>(content);
            
            if (data == null || data.Data == null) throw new Exception("Invalid response from Xiaomi API");

            double balance = data.Data.Balance;
            // Assuming quota is available in response or unlimited
            double quota = data.Data.Quota; 
            
            // If quota is 0, treat as pay-as-you-go balance only
            double percentage = 0;
            var used = quota > 0 ? Math.Max(0, quota - balance) : 0;
            if (quota > 0)
            {
                percentage = UsageMath.CalculateRemainingPercent(used, quota);
            }

            return new[] { new ProviderUsage
            {
                ProviderId = config.ProviderId,
                ProviderName = "Xiaomi",
                RequestsPercentage = percentage,
                RequestsUsed = used,
                RequestsAvailable = quota > 0 ? quota : balance, 
                UsageUnit = "Points", // or CNY
                IsQuotaBased = quota > 0,
                PlanType = quota > 0 ? PlanType.Coding : PlanType.Usage,
                IsAvailable = true,
                Description = quota > 0 
                    ? $"{balance} remaining / {quota} total" 
                    : $"Balance: {balance}"
            }};
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Xiaomi usage");
            return new[] { new ProviderUsage
            {
                ProviderId = config.ProviderId,
                ProviderName = "Xiaomi",
                IsAvailable = false,
                Description = $"Error: {ex.Message}"
            }};
        }
    }

    private class XiaomiResponse
    {
         [JsonPropertyName("data")]
         public XiaomiData? Data { get; set; }
         
         [JsonPropertyName("code")]
         public int Code { get; set; }
    }
    
    private class XiaomiData
    {
        [JsonPropertyName("balance")]
        public double Balance { get; set; }
        
        [JsonPropertyName("quota")]
        public double Quota { get; set; }
    }
}
