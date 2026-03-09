// <copyright file="IUsageAnalyticsService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Interfaces
{
    using AIUsageTracker.Core.Models;

    public interface IUsageAnalyticsService
    {
        Task<IReadOnlyDictionary<string, BurnRateForecast>> GetBurnRateForecastsAsync(
            IEnumerable<string> providerIds,
            int lookbackHours = 72,
            int maxSamplesPerProvider = 720);

        Task<IReadOnlyDictionary<string, ProviderReliabilitySnapshot>> GetProviderReliabilityAsync(
            IEnumerable<string> providerIds,
            int lookbackHours = 168,
            int maxSamplesPerProvider = 1000);

        Task<IReadOnlyDictionary<string, UsageAnomalySnapshot>> GetUsageAnomaliesAsync(
            IEnumerable<string> providerIds,
            int lookbackHours = 72,
            int maxSamplesPerProvider = 720);

        Task<IReadOnlyList<BudgetStatus>> GetBudgetStatusesAsync(IEnumerable<string> providerIds);

        Task<IReadOnlyList<UsageComparison>> GetUsageComparisonsAsync(IEnumerable<string> providerIds);
    }
}
