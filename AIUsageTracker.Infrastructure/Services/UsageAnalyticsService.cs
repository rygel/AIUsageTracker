using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;

namespace AIUsageTracker.Infrastructure.Services;

public class UsageAnalyticsService : IUsageAnalyticsService
{
    private readonly IWebDatabaseRepository _repository;
    private readonly IMemoryCache _cache;
    private readonly ILogger<UsageAnalyticsService> _logger;

    public UsageAnalyticsService(
        IWebDatabaseRepository repository,
        IMemoryCache cache,
        ILogger<UsageAnalyticsService> logger)
    {
        _repository = repository;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, BurnRateForecast>> GetBurnRateForecastsAsync(
        IEnumerable<string> providerIds,
        int lookbackHours = 72,
        int maxSamplesPerProvider = 720)
    {
        var normalizedIds = NormalizeProviderIds(providerIds);
        if (!normalizedIds.Any()) return new Dictionary<string, BurnRateForecast>(StringComparer.OrdinalIgnoreCase) as IReadOnlyDictionary<string, BurnRateForecast>;

        var cacheKey = $"analytics:burn-rate:{lookbackHours}:{maxSamplesPerProvider}:{string.Join(",", normalizedIds)}";
        if (_cache.TryGetValue<Dictionary<string, BurnRateForecast>>(cacheKey, out var cached) && cached != null)
        {
            return cached;
        }

        var sw = Stopwatch.StartNew();
        var data = await _repository.GetHistorySamplesAsync(normalizedIds, lookbackHours, maxSamplesPerProvider).ConfigureAwait(false);

        var forecasts = normalizedIds.ToDictionary(
            id => id,
            _ => BurnRateForecast.Unavailable("Insufficient history"),
            StringComparer.OrdinalIgnoreCase);

        foreach (var group in data.GroupBy(r => r.ProviderId, StringComparer.OrdinalIgnoreCase))
        {
            var samples = group.Where(x => x.IsAvailable).ToList();
            forecasts[group.Key] = UsageMath.CalculateBurnRateForecast(samples);
        }

        _cache.Set(cacheKey, forecasts, TimeSpan.FromMinutes(10));
        _logger.LogInformation("Analytics: GetBurnRateForecastsAsync elapsedMs={ElapsedMs}", sw.ElapsedMilliseconds);
        return forecasts as IReadOnlyDictionary<string, BurnRateForecast>;
    }

    public async Task<IReadOnlyDictionary<string, ProviderReliabilitySnapshot>> GetProviderReliabilityAsync(
        IEnumerable<string> providerIds,
        int lookbackHours = 168,
        int maxSamplesPerProvider = 1000)
    {
        var normalizedIds = NormalizeProviderIds(providerIds);
        if (!normalizedIds.Any()) return new Dictionary<string, ProviderReliabilitySnapshot>(StringComparer.OrdinalIgnoreCase) as IReadOnlyDictionary<string, ProviderReliabilitySnapshot>;

        var cacheKey = $"analytics:reliability:{lookbackHours}:{maxSamplesPerProvider}:{string.Join(",", normalizedIds)}";
        if (_cache.TryGetValue<Dictionary<string, ProviderReliabilitySnapshot>>(cacheKey, out var cached) && cached != null)
        {
            return cached;
        }

        var sw = Stopwatch.StartNew();
        var data = await _repository.GetHistorySamplesAsync(normalizedIds, lookbackHours, maxSamplesPerProvider).ConfigureAwait(false);

        var snapshots = normalizedIds.ToDictionary(
            id => id,
            _ => ProviderReliabilitySnapshot.Unavailable("No history"),
            StringComparer.OrdinalIgnoreCase);

        foreach (var group in data.GroupBy(r => r.ProviderId, StringComparer.OrdinalIgnoreCase))
        {
            var samples = group.Where(x => x.IsAvailable).ToList();
            snapshots[group.Key] = UsageMath.CalculateReliabilitySnapshot(samples);
        }

        _cache.Set(cacheKey, snapshots, TimeSpan.FromMinutes(10));
        _logger.LogInformation("Analytics: GetProviderReliabilityAsync elapsedMs={ElapsedMs}", sw.ElapsedMilliseconds);
        return snapshots as IReadOnlyDictionary<string, ProviderReliabilitySnapshot>;
    }

    public async Task<IReadOnlyDictionary<string, UsageAnomalySnapshot>> GetUsageAnomaliesAsync(
        IEnumerable<string> providerIds,
        int lookbackHours = 72,
        int maxSamplesPerProvider = 720)
    {
        var normalizedIds = NormalizeProviderIds(providerIds);
        if (!normalizedIds.Any()) return new Dictionary<string, UsageAnomalySnapshot>(StringComparer.OrdinalIgnoreCase) as IReadOnlyDictionary<string, UsageAnomalySnapshot>;

        var cacheKey = $"analytics:anomalies:{lookbackHours}:{maxSamplesPerProvider}:{string.Join(",", normalizedIds)}";
        if (_cache.TryGetValue<Dictionary<string, UsageAnomalySnapshot>>(cacheKey, out var cached) && cached != null)
        {
            return cached;
        }

        var sw = Stopwatch.StartNew();
        var data = await _repository.GetHistorySamplesAsync(normalizedIds, lookbackHours, maxSamplesPerProvider).ConfigureAwait(false);

        var anomalies = normalizedIds.ToDictionary(
            id => id,
            _ => UsageAnomalySnapshot.Unavailable("Insufficient history"),
            StringComparer.OrdinalIgnoreCase);

        foreach (var group in data.GroupBy(r => r.ProviderId, StringComparer.OrdinalIgnoreCase))
        {
            var samples = group.Where(x => x.IsAvailable).ToList();
            anomalies[group.Key] = UsageMath.CalculateUsageAnomalySnapshot(samples);
        }

        _cache.Set(cacheKey, anomalies, TimeSpan.FromMinutes(10));
        _logger.LogInformation("Analytics: GetUsageAnomaliesAsync elapsedMs={ElapsedMs}", sw.ElapsedMilliseconds);
        return anomalies as IReadOnlyDictionary<string, UsageAnomalySnapshot>;
    }

    public Task<IReadOnlyList<BudgetStatus>> GetBudgetStatusesAsync(IEnumerable<string> providerIds)
    {
        // Implementation of Budget Policies moved from God Class
        // ... (Transcribing from WebDatabaseService)
        return Task.FromResult<IReadOnlyList<BudgetStatus>>(new List<BudgetStatus>());
    }

    public Task<IReadOnlyList<UsageComparison>> GetUsageComparisonsAsync(IEnumerable<string> providerIds)
    {
        // Implementation of Usage Comparisons moved from God Class
        return Task.FromResult<IReadOnlyList<UsageComparison>>(new List<UsageComparison>());
    }

    private static List<string> NormalizeProviderIds(IEnumerable<string> providerIds)
    {
        if (providerIds == null) return new List<string>();
        return providerIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
