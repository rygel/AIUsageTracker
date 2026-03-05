using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Core.Interfaces;

public interface IWebDatabaseRepository
{
    Task<List<ProviderUsage>> GetHistorySamplesAsync(IEnumerable<string> providerIds, int lookbackHours, int maxSamples);
    Task<List<ProviderUsage>> GetAllHistoryForExportAsync(int limit = 0);
}
