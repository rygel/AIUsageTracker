using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Providers;

public class SimulatedProvider : IProviderService
{
    public string ProviderId => "simulated";
    private readonly ILogger<SimulatedProvider> _logger;

    public SimulatedProvider(ILogger<SimulatedProvider> logger)
    {
        _logger = logger;
    }

    public async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        // Simulate delay like Rust version
        await Task.Delay(500);

        return new[]
        {
            new ProviderUsage
            {
                ProviderId = ProviderId,
                ProviderName = "Simulated Provider",
                RequestsPercentage = 45.5,
                RequestsUsed = 12.50,
                RequestsAvailable = 100.0,
                IsQuotaBased = true,
                PlanType = PlanType.Coding,
                Description = "45% Used",
                UsageUnit = "USD",
                IsAvailable = true,
                AuthSource = config.AuthSource
            }
        };
    }
}

