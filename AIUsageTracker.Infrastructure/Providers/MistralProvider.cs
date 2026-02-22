using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Infrastructure.Providers;

public class MistralProvider : IProviderService
{
    public string ProviderId => "mistral";
    private readonly HttpClient _httpClient;
    private readonly ILogger<MistralProvider> _logger;

    public MistralProvider(HttpClient httpClient, ILogger<MistralProvider> logger)
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
                ProviderName = "Mistral AI",
                IsAvailable = false,
                Description = "API Key missing"
            }};
        }

        // Mistral does not have a public usage/billing API endpoint
        // We verify the key works by calling the models list endpoint
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.mistral.ai/v1/models");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                return new[] { new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "Mistral AI",
                    IsAvailable = true,
                    RequestsPercentage = 0,
                    IsQuotaBased = false,
                    PlanType = PlanType.Usage,
                    Description = "Connected (Check Dashboard)",
                    UsageUnit = "Status"
                }};
            }
            else
            {
                return new[] { new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "Mistral AI",
                    IsAvailable = false,
                    Description = $"Invalid API Key ({response.StatusCode})"
                }};
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify Mistral API key");
            return new[] { new ProviderUsage
            {
                ProviderId = ProviderId,
                ProviderName = "Mistral AI",
                IsAvailable = false,
                Description = "Connection Failed"
            }};
        }
    }
}

