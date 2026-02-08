using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;

namespace AIConsumptionTracker.Infrastructure.Providers;

public class OpenAIProvider : IProviderService
{
    public string ProviderId => "openai";
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenAIProvider> _logger;

    public OpenAIProvider(HttpClient httpClient, ILogger<OpenAIProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        // Validation
        if (string.IsNullOrEmpty(config.ApiKey)) throw new ArgumentException("API Key is missing via environment variable or auth.json");

        if (config.ApiKey.StartsWith("sk-proj"))
        {
             // Project keys not supported yet
             return new[] { new ProviderUsage
             {
                 ProviderId = ProviderId,
                 ProviderName = "OpenAI",
                 IsAvailable = false,
                 Description = "Project keys (sk-proj-...) not supported yet. Use a standard user API key."
             }};
        }


        // OpenAI's "Usage" API is deprecated/removed for API keys directly in some contexts, but let's try standard known logical endpoints or just link to dashboard.
        // Actually, OpenAI does not expose a public API for "Current Credit Balance" easily via standard keys anymore (Project keys etc).
        // However, many tools use a "subscription" or "usage" endpoint if available.
        // Let's implement a "Check Dashboard" approach if the API fails, but try a common endpoint first.
        
        // Strategy: Just check validity first by listing models. If valid, say "Connected".
        // Real usage tracking relies on the hidden/undocumented dashboard API which might fail.
        
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ApiKey);
             var response = await _httpClient.SendAsync(request);
             
             if (response.IsSuccessStatusCode)
             {
                 return new[] { new ProviderUsage
                 {
                    ProviderId = ProviderId,
                    ProviderName = "OpenAI",
                    IsAvailable = true,
                    UsagePercentage = 0,
                    IsQuotaBased = false,
                    PaymentType = PaymentType.UsageBased,
                    Description = "Connected (Check Dashboard)",

                    UsageUnit = "Status"
                 }};
             }
             else
             {
                 return new[] { new ProviderUsage
                 {
                    ProviderId = ProviderId,
                    ProviderName = "OpenAI",
                    IsAvailable = false,
                    Description = $"Invalid Key ({response.StatusCode})"
                 }};
             }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OpenAI check failed");
             return new[] { new ProviderUsage
             {
                ProviderId = ProviderId,
                ProviderName = "OpenAI",
                IsAvailable = false,
                Description = "Connection Failed"
             }};
        }
    }
}
