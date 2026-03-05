using System.Collections.Generic;
using System.Threading.Tasks;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Core.Interfaces;

public interface IWebDatabaseRepository
{
    bool IsDatabaseAvailable();
    Task<List<ProviderUsage>> GetLatestUsageAsync(bool includeInactive = false);
    Task<List<ProviderUsage>> GetHistoryAsync(int limit = 100);
    Task<List<ProviderUsage>> GetProviderHistoryAsync(string providerId, int limit = 100);
    Task<List<ProviderInfo>> GetProvidersAsync();
    Task<List<ResetEvent>> GetResetEventsAsync(string providerId, int limit = 50);
    Task<UsageSummary> GetUsageSummaryAsync();
    Task<List<ChartDataPoint>> GetChartDataAsync(int hours = 24);
    Task<List<ResetEvent>> GetRecentResetEventsAsync(int hours = 24);
    
    // Support for Analytics and Export
    Task<IEnumerable<ProviderUsage>> GetHistorySamplesAsync(IEnumerable<string> providerIds, int lookbackHours, int maxSamplesPerProvider);
    Task<IEnumerable<dynamic>> GetAllHistoryForExportAsync(int? limit = null);
}
