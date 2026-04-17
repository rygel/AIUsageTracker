// <copyright file="ProviderUsagePersistenceService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Monitor.Services;

public sealed class ProviderUsagePersistenceService
{
    private readonly IUsageDatabase _database;
    private readonly CachedGroupedUsageProjectionService? _groupedUsageProjectionCache;
    private readonly ILogger<ProviderUsagePersistenceService> _logger;

    public ProviderUsagePersistenceService(
        IUsageDatabase database,
        ILogger<ProviderUsagePersistenceService> logger,
        CachedGroupedUsageProjectionService? groupedUsageProjectionCache = null)
    {
        this._database = database;
        this._groupedUsageProjectionCache = groupedUsageProjectionCache;
        this._logger = logger;
    }

    public async Task PersistUsageAndDynamicProvidersAsync(List<ProviderUsage> filteredUsages, HashSet<string> activeProviderIds)
    {
        ArgumentNullException.ThrowIfNull(filteredUsages);
        ArgumentNullException.ThrowIfNull(activeProviderIds);

        await this.UpsertDynamicProvidersAsync(filteredUsages, activeProviderIds).ConfigureAwait(false);
        await this.StoreUsageHistoryAndSnapshotsAsync(filteredUsages).ConfigureAwait(false);
    }

    internal async Task UpsertDynamicProvidersAsync(List<ProviderUsage> filteredUsages, HashSet<string> activeProviderIds)
    {
        foreach (var usage in filteredUsages)
        {
            var isKnownActiveProvider = activeProviderIds.Contains(usage.ProviderId);
            if (!IsDynamicChildOfAnyActiveProvider(activeProviderIds, usage.ProviderId) &&
                isKnownActiveProvider)
            {
                continue;
            }

            if (!isKnownActiveProvider)
            {
                this._logger.LogInformation("Auto-registering dynamic provider: {ProviderId}", usage.ProviderId);
            }

            var dynamicConfig = new ProviderConfig
            {
                ProviderId = usage.ProviderId,
                AuthSource = usage.AuthSource,
                ApiKey = "dynamic",
            };

            await this._database.StoreProviderAsync(dynamicConfig, usage.ProviderName).ConfigureAwait(false);
            if (!isKnownActiveProvider)
            {
                activeProviderIds.Add(usage.ProviderId);
            }
        }
    }

    internal async Task StoreUsageHistoryAndSnapshotsAsync(List<ProviderUsage> filteredUsages)
    {
        await this._database.StoreHistoryAsync(filteredUsages).ConfigureAwait(false);
        this._groupedUsageProjectionCache?.Invalidate();
        this._logger.LogDebug("Stored {Count} provider histories", filteredUsages.Count);

        foreach (var usage in filteredUsages.Where(u => !string.IsNullOrEmpty(u.RawJson)))
        {
            await this._database.StoreRawSnapshotAsync(usage.ProviderId, usage.RawJson!, usage.HttpStatus).ConfigureAwait(false);
        }
    }

    private static bool IsDynamicChildOfAnyActiveProvider(HashSet<string> activeProviderIds, string usageProviderId)
    {
        return activeProviderIds.Any(providerId =>
            (ProviderMetadataCatalog.Find(providerId)?.IsChildProviderId(usageProviderId) ?? false));
    }
}
