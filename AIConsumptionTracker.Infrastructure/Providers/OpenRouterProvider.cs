using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;

namespace AIConsumptionTracker.Infrastructure.Providers;

public class OpenRouterProvider : IProviderService
{
    public string ProviderId => "openrouter";
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenRouterProvider> _logger;

    public OpenRouterProvider(HttpClient httpClient, ILogger<OpenRouterProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        _logger.LogDebug("Starting OpenRouter usage fetch for provider {ProviderId}", config.ProviderId);

        if (string.IsNullOrEmpty(config.ApiKey))
        {
            _logger.LogWarning("OpenRouter API key is missing for provider {ProviderId}", config.ProviderId);
            return new[] { new ProviderUsage
            {
                ProviderId = config.ProviderId,
                ProviderName = "OpenRouter",
                IsAvailable = false,
                Description = "API Key missing - please configure OPENROUTER_API_KEY"
            }};
        }

        // Try to fetch credits first
        OpenRouterCreditsResponse? creditsData = null;
        string? creditsResponseBody = null;
        
        try
        {
            _logger.LogDebug("Calling OpenRouter credits API: https://openrouter.ai/api/v1/credits");
            
            var request = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/credits");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);

            var response = await _httpClient.SendAsync(request);
            creditsResponseBody = await response.Content.ReadAsStringAsync();
            
            _logger.LogDebug("OpenRouter credits API response status: {StatusCode}", response.StatusCode);
            _logger.LogTrace("OpenRouter credits API response body: {ResponseBody}", creditsResponseBody);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("OpenRouter credits API failed with status {StatusCode}. Response: {Response}", 
                    response.StatusCode, creditsResponseBody);
                return new[] { new ProviderUsage
                {
                    ProviderId = config.ProviderId,
                    ProviderName = "OpenRouter",
                    IsAvailable = false,
                    Description = $"API Error: {response.StatusCode} - Check API key validity"
                }};
            }

            try
            {
                creditsData = System.Text.Json.JsonSerializer.Deserialize<OpenRouterCreditsResponse>(creditsResponseBody);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize OpenRouter credits response. Raw response: {Response}", creditsResponseBody);
                return new[] { new ProviderUsage
                {
                    ProviderId = config.ProviderId,
                    ProviderName = "OpenRouter",
                    IsAvailable = false,
                    Description = "Failed to parse credits response - API format may have changed"
                }};
            }
            
            if (creditsData?.Data == null)
            {
                _logger.LogError("OpenRouter credits response missing 'data' field. Response: {Response}", creditsResponseBody);
                return new[] { new ProviderUsage
                {
                    ProviderId = config.ProviderId,
                    ProviderName = "OpenRouter",
                    IsAvailable = false,
                    Description = "Invalid response format - missing data field"
                }};
            }

            _logger.LogDebug("Successfully parsed credits data - Total: {Total}, Usage: {Usage}", 
                creditsData.Data.TotalCredits, creditsData.Data.TotalUsage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while calling OpenRouter credits API");
            return new[] { new ProviderUsage
            {
                ProviderId = config.ProviderId,
                ProviderName = "OpenRouter",
                IsAvailable = false,
                Description = $"Connection error: {ex.Message}"
            }};
        }

        // Try to fetch additional key info (optional - for limits, labels, etc.)
        var details = new List<ProviderUsageDetail>();
        string label = "OpenRouter";
        
        try 
        {
            _logger.LogDebug("Calling OpenRouter key API: https://openrouter.ai/api/v1/key");
            
            var keyRequest = new HttpRequestMessage(HttpMethod.Get, "https://openrouter.ai/api/v1/key");
            keyRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);
            
            var keyResponse = await _httpClient.SendAsync(keyRequest);
            var keyResponseBody = await keyResponse.Content.ReadAsStringAsync();
            
            _logger.LogDebug("OpenRouter key API response status: {StatusCode}", keyResponse.StatusCode);
            _logger.LogTrace("OpenRouter key API response body: {ResponseBody}", keyResponseBody);
            
            if (keyResponse.IsSuccessStatusCode)
            {
                OpenRouterKeyResponse? keyData = null;
                
                try
                {
                    keyData = System.Text.Json.JsonSerializer.Deserialize<OpenRouterKeyResponse>(keyResponseBody);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize OpenRouter key response. Response: {Response}", keyResponseBody);
                }
                
                if (keyData?.Data != null)
                {
                    label = keyData.Data.Label ?? "OpenRouter";
                    _logger.LogDebug("OpenRouter key label: {Label}, Limit: {Limit}, IsFreeTier: {IsFreeTier}",
                        label, keyData.Data.Limit, keyData.Data.IsFreeTier);
                    
                    if (keyData.Data.Limit > 0)
                    {
                        string resetStr = "";
                        DateTime? nextResetTime = null;
                        
                        if (!string.IsNullOrEmpty(keyData.Data.LimitReset))
                        {
                            _logger.LogDebug("Parsing limit reset time: {LimitReset}", keyData.Data.LimitReset);
                            
                            if (DateTime.TryParse(keyData.Data.LimitReset, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt))
                            {
                                var diff = dt.ToLocalTime() - DateTime.Now;
                                if (diff.TotalSeconds > 0)
                                {
                                    resetStr = $" (Resets: ({dt.ToLocalTime():MMM dd HH:mm}))";
                                    nextResetTime = dt.ToLocalTime();
                                    _logger.LogDebug("Limit reset time parsed successfully: {ResetTime}", nextResetTime);
                                }
                            }
                            else
                            {
                                _logger.LogWarning("Failed to parse limit reset time: {LimitReset}", keyData.Data.LimitReset);
                            }
                        }

                        details.Add(new ProviderUsageDetail 
                        { 
                            Name = "Spending Limit", 
                            Description = $"{keyData.Data.Limit.ToString("F2", CultureInfo.InvariantCulture)}{resetStr}", 
                            Used = "",
                            NextResetTime = nextResetTime
                        });
                    }
                    else
                    {
                        _logger.LogDebug("No spending limit set for this key");
                    }
                    
                    details.Add(new ProviderUsageDetail { 
                        Name = "Free Tier", 
                        Description = keyData.Data.IsFreeTier ? "Yes" : "No", 
                        Used = "" 
                    });
                }
                else
                {
                    _logger.LogWarning("OpenRouter key API response missing 'data' field. Response: {Response}", keyResponseBody);
                }
            }
            else
            {
                _logger.LogWarning("OpenRouter key API returned {StatusCode}. Key info unavailable. Response: {Response}",
                    keyResponse.StatusCode, keyResponseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exception while calling OpenRouter key API - continuing with credits data only");
        }

        // Calculate usage statistics
        var total = creditsData.Data.TotalCredits;
        var used = creditsData.Data.TotalUsage;
        var remainingPercentage = UsageMath.CalculateRemainingPercent(used, total);
        var remaining = total - used;
        
        _logger.LogInformation("OpenRouter usage calculated - Total: {Total}, Used: {Used}, Remaining: {Remaining}, RemainingPercentage: {RemainingPercentage}%",
            total, used, remaining, remainingPercentage);
        
        string mainReset = "";
        var spendingLimitDetail = details.FirstOrDefault(d => d.Name == "Spending Limit");
        if (spendingLimitDetail != null && spendingLimitDetail.Description.Contains("(Resets:"))
        {
            var idx = spendingLimitDetail.Description.IndexOf("(Resets:");
            if (idx >= 0) mainReset = " " + spendingLimitDetail.Description.Substring(idx);
        }

        return new[] { new ProviderUsage
        {
            ProviderId = config.ProviderId,
            ProviderName = label,
            RequestsPercentage = remainingPercentage,
            RequestsUsed = used,
            RequestsAvailable = total,
            PlanType = PlanType.Usage,
            UsageUnit = "Credits",
            IsQuotaBased = true,
            IsAvailable = true,
            Description = $"{remaining.ToString("F2", CultureInfo.InvariantCulture)} Credits Remaining{mainReset}",
            NextResetTime = spendingLimitDetail?.NextResetTime,
            Details = details
        }};
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
