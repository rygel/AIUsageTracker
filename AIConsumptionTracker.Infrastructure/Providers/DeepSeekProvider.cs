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

    public async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config)
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
            
            if (result == null || !result.IsAvailable)
            {
                return new[] { new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "DeepSeek",
                    IsAvailable = false,
                    Description = "Account unavailable or parsing failed"
                }};
            }

            // DeepSeek can return multiple "balance_infos". Usually CNY or USD.
            // We'll sum them up or pick the main one. Typically users use USD or CNY.
            // Let's just specific the first currency found or sum total balance if they use consistent units (unlikely).
            // Usually it's just one currency per account type.
            
            var mainBalance = result.BalanceInfos?.FirstOrDefault();
            if (mainBalance == null)
            {
                return new[] { new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "DeepSeek",
                    IsAvailable = true,
                    Description = "No balance info found",
                }};
            }
            
            // "topped_up_balance" + "granted_balance" = Total available?
            // "total_balance" seems to be the sum.
            
            // For DeepSeek, it's a "Balance" model (Pre-paid).
            // Usage % is not quite relevant unless we know a "Low Balance" threshold.
            // Or we treat it as "0% used" and just show the balance text.
            // But to be consistent with other providers that show red when low, maybe we invert logic?
            // "UsagePercentage" usually fills the bar.
            // Let's keep usage at 0 and just show text, or maybe fake a "100 unit" scale?
            // Better to show green bar if balance > 0?
            // Actually, if it's usage tracking, people usually want to know how much they spent *this month*.
            // But /user/balance endpoint only gives current balance.
            // So we display Balance.
            
            string currencySymbol = mainBalance.Currency == "CNY" ? "¥" : "$";
            string balanceText = $"{currencySymbol}{mainBalance.TotalBalance:F2}";
            
            return new[] { new ProviderUsage
            {
                ProviderId = ProviderId,
                ProviderName = "DeepSeek",
                IsAvailable = true,
                UsagePercentage = 0, // Balance model, bar empty or maybe full green? Let's leave empty.
                CostUsed = 0,
                CostLimit = 0,
                UsageUnit = "Currency",
                IsQuotaBased = false,
                PaymentType = PaymentType.Credits,
                Description = $"Balance: {balanceText}"

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
