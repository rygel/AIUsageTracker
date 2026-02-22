using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class MinimaxProvider : GenericPayAsYouGoProvider
{
    public override string ProviderId => "minimax";

    public MinimaxProvider(HttpClient httpClient, ILogger<MinimaxProvider> logger) : base(httpClient, logger)
    {
    }

    public override async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            throw new ArgumentException("API Key not found.");
        }

        string url;
        // Prioritize BaseUrl if set
        if (!string.IsNullOrEmpty(config.BaseUrl))
        {
            url = config.BaseUrl;
            if (!url.StartsWith("http")) url = "https://" + url;
        }
        else
        {
            // Determine endpoint based on ID suffix
            if (config.ProviderId.EndsWith("-io", StringComparison.OrdinalIgnoreCase) || 
                config.ProviderId.EndsWith("-global", StringComparison.OrdinalIgnoreCase))
            {
               url = "https://api.minimax.io/v1/user/usage";
            }
            else
            {
               url = "https://api.minimax.chat/v1/user/usage";
            }
        }

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);

        var response = await _httpClient.SendAsync(request);
        
        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"API returned {response.StatusCode} for {url}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        
        double used = 0;
        double total = 0;
        PlanType paymentType = PlanType.Usage;

        try
        {
            var minimax = JsonSerializer.Deserialize<MinimaxResponse>(responseString);
            if (minimax?.Usage != null)
            {
                used = minimax.Usage.TokensUsed;
                total = minimax.Usage.TokensLimit > 0 ? minimax.Usage.TokensLimit : 0; 
                paymentType = PlanType.Usage;
            }
            else
            {
                 throw new Exception("Invalid Minimax response format");
            }
        }
        catch (JsonException ex)
        {
             throw new Exception($"Failed to parse Minimax response: {ex.Message}");
        }

        var utilization = total > 0 ? (used / total) * 100.0 : 0;

        return new[] { new ProviderUsage
        {
            ProviderId = config.ProviderId,
            ProviderName = "Minimax",
            RequestsPercentage = Math.Min(utilization, 100),
            RequestsUsed = used,
            RequestsAvailable = total,
            PlanType = paymentType,
            UsageUnit = "Tokens", 
            IsQuotaBased = false,
            Description = $"{used:N0} tokens used" + (total > 0 ? $" / {total:N0} limit" : "")
        }};
    }
}

