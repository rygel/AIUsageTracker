using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;

namespace AIConsumptionTracker.Infrastructure.Providers;

public class SimulatedProvider : IProviderService
{
    public string ProviderId => "simulated";

    public async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config)
    {
        // Simulate network delay
        await Task.Delay(500);

        return new[] { new ProviderUsage
        {
            ProviderId = ProviderId,
            ProviderName = "Simulated Provider",
            UsagePercentage = 45.5,
            CostUsed = 12.50,
            CostLimit = 100.0,
            IsQuotaBased = true,
            Description = "45% Used"
        }};
    }
}

