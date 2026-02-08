using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;

namespace AIConsumptionTracker.Infrastructure.Providers;

public class DeepSeekProvider : IProviderService
{
    public string ProviderId => "deepseek";
    private readonly HttpClient _httpClient;
    private readonly ILogger<DeepSeekProvider> _logger;

    public DeepSeekProvider(HttpClient httpClient, ILogger<DeepSeekProvider> logger)
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
                ProviderId = ProviderId,
                ProviderName = "DeepSeek",
                IsAvailable = false,
                Description = "API Key missing"
            }};
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.deepseek.com/user/balance");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);
            request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning($"DeepSeek API error: {response.StatusCode} - {errorContent}");
                
                return new[] { new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "DeepSeek",
                    IsAvailable = true, // Key exists, just failed request
                    Description = $"API Error ({response.StatusCode})",
                    UsagePercentage = 0,
                    IsQuotaBased = false
                }};
            }

            var result = await response.Content.ReadFromJsonAsync<DeepSeekBalanceResponse>();
            
            if (result == null)
            {
                return new[] { new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "DeepSeek",
                    IsAvailable = false,
                    Description = "Failed to parse DeepSeek response"
                }};
            }

            var details = new List<ProviderUsageDetail>();
            double totalBalanceValue = 0;
            string mainDescription = "No balance found";

            if (result.BalanceInfos != null && result.BalanceInfos.Any())
            {
                foreach (var info in result.BalanceInfos)
                {
                    string currencySymbol = info.Currency == "CNY" ? "¥" : "$";
                    string detailName = $"Balance ({info.Currency})";
                    string detailDesc = $"{currencySymbol}{info.ToppedUpBalance.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)} (Topped-up: {currencySymbol}{info.ToppedUpBalance.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}, Granted: {currencySymbol}{info.GrantedBalance.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)})";
                    
                    details.Add(new ProviderUsageDetail
                    {
                        Name = detailName,
                        Used = info.Currency == "CNY" ? $"¥{info.TotalBalance.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}" : $"${info.TotalBalance.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}",
                        Description = detailDesc
                    });

                    // If it's the first or a primary currency, use for main description
                    if (mainDescription == "No balance found")
                    {
                        mainDescription = $"Balance: {currencySymbol}{info.TotalBalance.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}";
                    }
                }
            }

            return new[] { new ProviderUsage
            {
                ProviderId = ProviderId,
                ProviderName = "DeepSeek",
                IsAvailable = true,
                UsagePercentage = 0, // Pay-as-you-go/Balance model
                CostUsed = 0,
                CostLimit = 0,
                UsageUnit = "Currency",
                IsQuotaBased = false,
                PaymentType = PaymentType.UsageBased,
                Description = mainDescription,
                Details = details
            }};
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DeepSeek check failed");
            return new[] { new ProviderUsage
            {
                ProviderId = ProviderId,
                ProviderName = "DeepSeek",
                IsAvailable = false,
                Description = "Check failed"
            }};
        }
    }

    private class DeepSeekBalanceResponse
    {
        [JsonPropertyName("is_available")]
        public bool IsAvailable { get; set; }

        [JsonPropertyName("balance_infos")]
        public List<BalanceInfo>? BalanceInfos { get; set; }
    }

    private class BalanceInfo
    {
        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("total_balance")]
        public double TotalBalance { get; set; }

        [JsonPropertyName("granted_balance")]
        public double GrantedBalance { get; set; }

        [JsonPropertyName("topped_up_balance")]
        public double ToppedUpBalance { get; set; }
    }
}
