using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Infrastructure.Providers;

public class OpenCodeProvider : IProviderService
{
    public string ProviderId => "opencode";
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenCodeProvider> _logger;

    public OpenCodeProvider(HttpClient httpClient, ILogger<OpenCodeProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        _logger.LogDebug("OpenCode GetUsageAsync called for provider {ProviderId}", ProviderId);
        
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            _logger.LogInformation("OpenCode API key not configured - returning unavailable state");
            return new[] { new ProviderUsage
            {
                ProviderId = ProviderId,
                ProviderName = "Opencode Zen",
                IsAvailable = false,
                Description = "No API key configured",
                IsQuotaBased = false,
                PlanType = PlanType.Usage
            }};
        }

        try
        {
            var url = "https://api.opencode.ai/v1/credits";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);

            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("OpenCode API failed with status {StatusCode}", response.StatusCode);
                return new[] { new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "Opencode Zen",
                    IsAvailable = false,
                    Description = $"API Error: {response.StatusCode}",
                    IsQuotaBased = false,
                    PlanType = PlanType.Usage
                }};
            }

            var responseString = await response.Content.ReadAsStringAsync();
            var result = ParseJsonResponse(responseString);
            _logger.LogInformation("OpenCode usage retrieved successfully - Total Cost: ${TotalCost:F2}", result.RequestsUsed);
            
            return new[] { result };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to call OpenCode API. Message: {Message}", ex.Message);
            return new[] { new ProviderUsage
            {
                ProviderId = ProviderId,
                ProviderName = "Opencode Zen",
                IsAvailable = false,
                Description = $"API Call Failed: {ex.Message}",
                IsQuotaBased = false,
                PlanType = PlanType.Usage
            }};
        }
    }

private ProviderUsage ParseJsonResponse(string json)
    {
        try
        {
            // Check if response is empty or not JSON
            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.LogWarning("OpenCode API returned empty response");
                return new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "Opencode Zen",
                    IsAvailable = false,
                    Description = "Empty API response",
                    IsQuotaBased = false,
                    PlanType = PlanType.Usage
                };
            }

            // Log the raw response for debugging
            _logger.LogDebug("OpenCode raw response: {Response}", json.Length > 200 ? json.Substring(0, 200) + "..." : json);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var response = JsonSerializer.Deserialize<CreditsResponse>(json, options);
            
            double totalCost = 0;
            if (response?.Data != null)
            {
                totalCost = response.Data.UsedCredits;
            }

            return new ProviderUsage
            {
                ProviderId = ProviderId,
                ProviderName = "Opencode Zen",
                RequestsPercentage = 0,
                RequestsUsed = totalCost,
                UsageUnit = "USD",
                IsQuotaBased = false,
                PlanType = PlanType.Usage,
                IsAvailable = true,
                Description = $"${totalCost.ToString("F2", CultureInfo.InvariantCulture)} used (7 days)"
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse OpenCode JSON response. Response started with: {Start}", 
                json.Length > 50 ? json.Substring(0, 50) : json);
            return new ProviderUsage
            {
                ProviderId = ProviderId,
                ProviderName = "Opencode Zen",
                IsAvailable = false,
                Description = "Invalid API response (not JSON)",
                IsQuotaBased = false,
                PlanType = PlanType.Usage
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse OpenCode response");
            return new ProviderUsage
            {
                ProviderId = ProviderId,
                ProviderName = "Opencode Zen",
                IsAvailable = false,
                Description = "Parse Error",
                IsQuotaBased = false,
                PlanType = PlanType.Usage
            };
        }
    }

    private class CreditsResponse
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
    }

}

