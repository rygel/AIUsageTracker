using System.Net.Http.Json;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Infrastructure.Helpers;

namespace AIConsumptionTracker.Infrastructure.Providers;

public class GitHubCopilotProvider : IProviderService
{
    public string ProviderId => "github-copilot";
    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubCopilotProvider> _logger;

    public GitHubCopilotProvider(HttpClient httpClient, ILogger<GitHubCopilotProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config)
    {
        return Task.FromResult<IEnumerable<ProviderUsage>>(new[] { new ProviderUsage
        {
            ProviderId = ProviderId,
            ProviderName = "GitHub Copilot",
            IsAvailable = false,
            Description = "Browser session tracking (cookies) has been disabled for privacy."
        }});
    }
}
