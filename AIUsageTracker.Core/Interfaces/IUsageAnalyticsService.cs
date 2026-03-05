using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Core.Interfaces;

public interface IUsageAnalyticsService
{
    Task<Dictionary<string, BurnRateForecast>> GetBurnRateForecastsAsync(
        IEnumerable<string> providerIds,
        int lookbackHours = 72,
        int maxSamplesPerProvider = 720);

    Task<Dictionary<string, ProviderReliabilitySnapshot>> GetProviderReliabilityAsync(
        IEnumerable<string> providerIds,
        int lookbackHours = 168,
        int maxSamplesPerProvider = 1000);

    Task<Dictionary<string, UsageAnomalySnapshot>> GetUsageAnomaliesAsync(
        IEnumerable<string> providerIds,
        int lookbackHours = 72,
        int maxSamplesPerProvider = 720);

    Task<List<BudgetStatus>> GetBudgetStatusesAsync(List<string> providerIds);
    
    Task<List<UsageComparison>> GetUsageComparisonsAsync(List<string> providerIds);
}

public interface IDataExportService
{
    Task<string> ExportHistoryToCsvAsync();
    Task<string> ExportHistoryToJsonAsync();
    Task<byte[]?> CreateDatabaseBackupAsync();
}
