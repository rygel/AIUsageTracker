using Microsoft.Extensions.Logging;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;
using System.Net.Http.Json;

namespace AIConsumptionTracker.Infrastructure.Providers;

public class CodexProvider : IProviderService
{
    public string ProviderId => "codex";
    private readonly HttpClient _httpClient;
    private readonly ILogger<CodexProvider> _logger;

    public CodexProvider(HttpClient httpClient, ILogger<CodexProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config)
    {
        // Codex often uses the same backend/structure as OpenAI or is accessed via specific endpoints.
        // Assuming standard OpenAI-like usage structure for now or placeholder behavior until specific API details are confirmed.
        // If "Codex" refers specifically to the OpenAI model series, it might share the OpenAI provider logic.
        // For now, returning a placeholder or a basic implementation if an API key is present.

        if (string.IsNullOrEmpty(config.ApiKey))
        {
            return Enumerable.Empty<ProviderUsage>();
        }

        // Placeholder logic: If Codex has a specific separate API, we'd call it here.
        // If it's just a model under OpenAI, this provider might redundant or strictly for filtering.
        // Given the request "Support Codex", we'll treat it as a distinct entry for now.
        
        return new[] { new ProviderUsage
        {
            ProviderId = ProviderId,
            ProviderName = "Codex",
            CostUsed = 0, // Placeholder
            CostLimit = 0,
            UsagePercentage = 0,
            IsAvailable = true,
            Description = "Codex usage tracking (Implementation pending specific API details)"
        }};
    }
}
