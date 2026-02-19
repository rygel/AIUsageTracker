using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIConsumptionTracker.Infrastructure.Providers;

public class AnthropicProvider : IProviderService
{
    public string ProviderId => "anthropic";
    private readonly ILogger<AnthropicProvider> _logger;

    public AnthropicProvider(ILogger<AnthropicProvider> logger)
    {
        _logger = logger;
    }

    public async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            return new[]
            {
                new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "Anthropic",
                    IsAvailable = false,
                    Description = "API Key missing",
                    IsQuotaBased = false,
                    PlanType = PlanType.Usage,
                    AuthSource = config.AuthSource
                }
            };
        }

        return new[]
        {
            new ProviderUsage
            {
                ProviderId = ProviderId,
                ProviderName = "Anthropic",
                IsAvailable = true,
                RequestsPercentage = 0.0,
                IsQuotaBased = false,
                PlanType = PlanType.Usage,
                Description = "Connected (Check Dashboard)",
                UsageUnit = "Status",
                AuthSource = config.AuthSource
            }
        };
    }
}
