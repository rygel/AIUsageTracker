using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Monitor.Services;

public interface IUsageDatabase
{
    Task InitializeAsync();
    Task StoreProviderAsync(ProviderConfig config, string? friendlyName = null);
    Task StoreHistoryAsync(IEnumerable<ProviderUsage> usages);
    Task StoreRawSnapshotAsync(string providerId, string rawJson, int httpStatus);
    Task CleanupOldSnapshotsAsync();
    Task OptimizeAsync();
    Task StoreResetEventAsync(string providerId, string providerName, double? previousUsage, double? newUsage, string resetType);
    Task<List<ProviderUsage>> GetLatestHistoryAsync();
    Task<List<ProviderUsage>> GetHistoryAsync(int limit = 100);
    Task<List<ProviderUsage>> GetHistoryByProviderAsync(string providerId, int limit = 100);
    Task<List<ProviderUsage>> GetRecentHistoryAsync(int countPerProvider);
    Task<List<ResetEvent>> GetResetEventsAsync(string providerId, int limit = 50);
    Task<bool> IsHistoryEmptyAsync();
}


