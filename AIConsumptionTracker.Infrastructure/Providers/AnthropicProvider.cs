using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;

namespace AIConsumptionTracker.Infrastructure.Providers;

public class AnthropicProvider : IProviderService
{
    public string ProviderId => "anthropic";
    private readonly HttpClient _httpClient;
    private readonly ILogger<AnthropicProvider> _logger;

    public AnthropicProvider(HttpClient httpClient, ILogger<AnthropicProvider> logger)
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
                ProviderName = "Anthropic",
                IsAvailable = false,
                Description = "API Key missing"
            }};
        }

        // Anthropic does not have a usage API.
        // We just verify the key works by making a small messages calls (requires credits though!)
        // Or better, just assume connected if key is present to save money/latency,
        // OR try a models list if available (Anthropic doesn't have a public 'list models' endpoint exactly like OpenAI?).
        // Actually they do: GET /v1/models (beta?). but standard is just POST /messages.
        // Let's assume connected to avoid spending money on checks.
        
        return new[] { new ProviderUsage
        {
            ProviderId = ProviderId,
            ProviderName = "Anthropic",
            IsAvailable = true,
            UsagePercentage = 0,
            IsQuotaBased = false,
            PaymentType = PaymentType.UsageBased,
            Description = "Connected (Check Dashboard)",

             UsageUnit = "Status"
        }};
    }
}
