// <copyright file="CachedGroupedUsageProjectionService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.MonitorClient;

namespace AIUsageTracker.Monitor.Services;

internal sealed class CachedGroupedUsageProjectionService
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private readonly IUsageDatabase _database;
    private readonly object _lock = new();
    private AgentGroupedUsageSnapshot? _cachedSnapshot;
    private DateTime _cacheTimestamp = DateTime.MinValue;

    public CachedGroupedUsageProjectionService(IUsageDatabase database)
    {
        this._database = database;
    }

    public async Task<AgentGroupedUsageSnapshot> GetGroupedUsageAsync()
    {
        lock (this._lock)
        {
            if (this._cachedSnapshot != null && DateTime.UtcNow - this._cacheTimestamp < CacheDuration)
            {
                return this._cachedSnapshot;
            }
        }

        var usage = await this._database.GetLatestHistoryAsync().ConfigureAwait(false);
        var snapshot = GroupedUsageProjectionService.Build(usage);

        lock (this._lock)
        {
            this._cachedSnapshot = snapshot;
            this._cacheTimestamp = DateTime.UtcNow;
        }

        return snapshot;
    }

    public void Invalidate()
    {
        lock (this._lock)
        {
            this._cachedSnapshot = null;
            this._cacheTimestamp = DateTime.MinValue;
        }
    }
}
