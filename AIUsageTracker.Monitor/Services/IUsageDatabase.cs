using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Monitor.Services;

public interface IUsageDatabase
{
    Task InitializeAsync();
`n    Task StoreProviderAsync(ProviderConfig config, string? friendlyName = null);
`n    Task StoreHistoryAsync(IEnumerable<ProviderUsage> usages);
`n    Task StoreRawSnapshotAsync(string providerId, string rawJson, int httpStatus);
`n    Task CleanupOldSnapshotsAsync();
`n    Task OptimizeAsync();
`n    Task StoreResetEventAsync(string providerId, string providerName, double? previousUsage, double? newUsage, string resetType);
`n    Task<List<ProviderUsage>> GetLatestHistoryAsync();
`n    Task<List<ProviderUsage>> GetHistoryAsync(int limit = 100);
`n    Task<List<ProviderUsage>> GetHistoryByProviderAsync(string providerId, int limit = 100);
`n    Task<List<ProviderUsage>> GetRecentHistoryAsync(int countPerProvider);
`n    Task<List<ResetEvent>> GetResetEventsAsync(string providerId, int limit = 50);
`n    Task<bool> IsHistoryEmptyAsync();
`n    Task SetProviderActiveAsync(string providerId, bool isActive);
}

