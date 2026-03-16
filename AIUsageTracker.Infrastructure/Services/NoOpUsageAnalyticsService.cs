using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Infrastructure.Services;

public class NoOpUsageAnalyticsService : IUsageAnalyticsService
{
    public Task<IReadOnlyDictionary<string, BurnRateForecast>> GetBurnRateForecastsAsync(IEnumerable<string> providerIds, int lookbackHours = 72, int maxSamplesPerProvider = 720) => Task.FromResult<IReadOnlyDictionary<string, BurnRateForecast>>(new Dictionary<string, BurnRateForecast>());
    public Task<IReadOnlyDictionary<string, ProviderReliabilitySnapshot>> GetProviderReliabilityAsync(IEnumerable<string> providerIds, int lookbackHours = 168, int maxSamplesPerProvider = 1000) => Task.FromResult<IReadOnlyDictionary<string, ProviderReliabilitySnapshot>>(new Dictionary<string, ProviderReliabilitySnapshot>());
    public Task<IReadOnlyDictionary<string, UsageAnomalySnapshot>> GetUsageAnomaliesAsync(IEnumerable<string> providerIds, int lookbackHours = 72, int maxSamplesPerProvider = 720) => Task.FromResult<IReadOnlyDictionary<string, UsageAnomalySnapshot>>(new Dictionary<string, UsageAnomalySnapshot>());
    public Task<IReadOnlyList<BudgetStatus>> GetBudgetStatusesAsync(IEnumerable<string> providerIds) => Task.FromResult<IReadOnlyList<BudgetStatus>>(new List<BudgetStatus>());
    public Task<IReadOnlyList<UsageComparison>> GetUsageComparisonsAsync(IEnumerable<string> providerIds) => Task.FromResult<IReadOnlyList<UsageComparison>>(new List<UsageComparison>());
}
