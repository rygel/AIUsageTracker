using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Infrastructure.Providers;

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
                Description = "API Key missing",
                IsQuotaBased = false,
                PlanType = PlanType.Usage
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
                    RequestsPercentage = 0,
                    IsQuotaBased = false,
                    PlanType = PlanType.Usage
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
                    Description = "Failed to parse DeepSeek response",
                    IsQuotaBased = false,
                    PlanType = PlanType.Usage
                }};
            }

            var details = new List<ProviderUsageDetail>();
            string mainDescription = "No balance found";

            if (result.BalanceInfos != null && result.BalanceInfos.Any())
            {
                foreach (var info in result.BalanceInfos)
                {
                    string currencySymbol = info.Currency == "CNY" ? "¥" : "$";
                    string detailName = $"Balance ({info.Currency})";
                    details.Add(new ProviderUsageDetail
                    {
                        Name = detailName,
                        Used = $"{currencySymbol}{info.TotalBalance.ToString("F2", CultureInfo.InvariantCulture)}",
                        Description = $"{currencySymbol}{info.ToppedUpBalance.ToString("F2", CultureInfo.InvariantCulture)} (Topped-up: {currencySymbol}{info.ToppedUpBalance.ToString("F2", CultureInfo.InvariantCulture)}, Granted: {currencySymbol}{info.GrantedBalance.ToString("F2", CultureInfo.InvariantCulture)})"
                    });

                    // If it's the first or a primary currency, use for main description
                    if (mainDescription == "No balance found")
                    {
                        mainDescription = $"Balance: {currencySymbol}{info.TotalBalance.ToString("F2", CultureInfo.InvariantCulture)}";
                    }
                }
            }

            return new[] { new ProviderUsage
            {
                ProviderId = ProviderId,
                ProviderName = "DeepSeek",
                IsAvailable = true,
                RequestsPercentage = 0, // Pay-as-you-go/Balance model
                RequestsUsed = 0,
                RequestsAvailable = 0,
                UsageUnit = "Currency",
                IsQuotaBased = false,
                PlanType = PlanType.Usage,
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
                Description = "Check failed",
                IsQuotaBased = false,
                PlanType = PlanType.Usage
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

