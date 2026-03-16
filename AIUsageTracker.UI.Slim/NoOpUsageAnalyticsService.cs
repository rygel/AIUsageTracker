// <copyright file="NoOpUsageAnalyticsService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim;

public sealed class NoOpUsageAnalyticsService : IUsageAnalyticsService
{
    public Task<IReadOnlyDictionary<string, BurnRateForecast>> GetBurnRateForecastsAsync(
        IEnumerable<string> providerIds,
        int lookbackHours = 72,
        int maxSamplesPerProvider = 720) =>
        Task.FromResult<IReadOnlyDictionary<string, BurnRateForecast>>(
            new Dictionary<string, BurnRateForecast>(StringComparer.Ordinal));

    public Task<IReadOnlyDictionary<string, ProviderReliabilitySnapshot>> GetProviderReliabilityAsync(
        IEnumerable<string> providerIds,
        int lookbackHours = 168,
        int maxSamplesPerProvider = 1000) =>
        Task.FromResult<IReadOnlyDictionary<string, ProviderReliabilitySnapshot>>(
            new Dictionary<string, ProviderReliabilitySnapshot>(StringComparer.Ordinal));

    public Task<IReadOnlyDictionary<string, UsageAnomalySnapshot>> GetUsageAnomaliesAsync(
        IEnumerable<string> providerIds,
        int lookbackHours = 72,
        int maxSamplesPerProvider = 720) =>
        Task.FromResult<IReadOnlyDictionary<string, UsageAnomalySnapshot>>(
            new Dictionary<string, UsageAnomalySnapshot>(StringComparer.Ordinal));

    public Task<IReadOnlyList<UsageComparison>> GetUsageComparisonsAsync(IEnumerable<string> providerIds) =>
        Task.FromResult<IReadOnlyList<UsageComparison>>(new List<UsageComparison>());

    public Task<IReadOnlyList<BudgetStatus>> GetBudgetStatusesAsync(IEnumerable<string> providerIds) =>
        Task.FromResult<IReadOnlyList<BudgetStatus>>(new List<BudgetStatus>());
}
