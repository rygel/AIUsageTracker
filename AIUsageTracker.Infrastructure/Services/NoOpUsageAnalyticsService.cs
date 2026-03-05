using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Infrastructure.Services;

public class NoOpUsageAnalyticsService : IUsageAnalyticsService
{
    public Task<Dictionary<string, BurnRateForecast>> GetBurnRateForecastsAsync(IEnumerable<string> providerIds, int lookbackHours = 24, int maxSamplesPerProvider = 100) => Task.FromResult(new Dictionary<string, BurnRateForecast>());
    public Task<Dictionary<string, ProviderReliabilitySnapshot>> GetProviderReliabilityAsync(IEnumerable<string> providerIds, int lookbackHours = 24, int maxSamplesPerProvider = 100) => Task.FromResult(new Dictionary<string, ProviderReliabilitySnapshot>());
    public Task<Dictionary<string, UsageAnomalySnapshot>> GetUsageAnomaliesAsync(IEnumerable<string> providerIds, int lookbackHours = 24, int maxSamplesPerProvider = 100) => Task.FromResult(new Dictionary<string, UsageAnomalySnapshot>());
    public Task<List<UsageComparison>> GetUsageComparisonsAsync(List<string> providerIds) => Task.FromResult(new List<UsageComparison>());
    public Task<List<BudgetStatus>> GetBudgetStatusesAsync(List<string> providerIds) => Task.FromResult(new List<BudgetStatus>());
}
