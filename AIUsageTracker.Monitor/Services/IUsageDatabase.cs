// <copyright file="IUsageDatabase.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Monitor.Services;

public interface IUsageDatabase
{
    Task InitializeAsync();

    Task StoreProviderAsync(ProviderConfig config, string? friendlyName = null);

    Task StoreHistoryAsync(IEnumerable<ProviderUsage> usages);

    Task StoreRawSnapshotAsync(string providerId, string rawJson, int httpStatus);

    Task CleanupOldSnapshotsAsync();

    Task CompactHistoryAsync();

    Task OptimizeAsync();

    Task StoreResetEventAsync(string providerId, string providerName, double? previousUsage, double? newUsage, string resetType);

    Task<IReadOnlyList<ProviderUsage>> GetLatestHistoryAsync(IReadOnlyCollection<string>? providerIds = null);

    Task<IReadOnlyList<ProviderUsage>> GetHistoryAsync(int limit = 100);

    Task<IReadOnlyList<ProviderUsage>> GetHistoryByProviderAsync(string providerId, int limit = 100);

    Task<IReadOnlyList<ProviderUsage>> GetRecentHistoryAsync(int countPerProvider);

    Task<IReadOnlyList<ResetEvent>> GetResetEventsAsync(string providerId, int limit = 50);

    Task<bool> IsHistoryEmptyAsync();

    Task SetProviderActiveAsync(string providerId, bool isActive);
}
