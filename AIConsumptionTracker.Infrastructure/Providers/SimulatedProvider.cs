using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIConsumptionTracker.Infrastructure.Providers;

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
                UsagePercentage = 45.5,
                CostUsed = 12.50,
                CostLimit = 100.0,
                IsQuotaBased = true,
                PaymentType = PaymentType.Quota,
                Description = "45% Used",
                UsageUnit = "USD",
                IsAvailable = true,
                AuthSource = config.AuthSource
            }
        };
    }
}
