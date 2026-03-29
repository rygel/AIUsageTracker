// <copyright file="ProviderRefreshConfigSelector.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Monitor.Services;

public sealed class ProviderRefreshConfigSelector
{
    public ProviderRefreshConfigSelection SelectActiveConfigs(
        IReadOnlyCollection<ProviderConfig> configs,
        bool forceAll,
        IReadOnlyCollection<string>? includeProviderIds)
    {
        var activeConfigs = configs
            .Where(config =>
                ProviderMetadataCatalog.ShouldPersistProviderId(config.ProviderId) &&
                (forceAll || !string.IsNullOrEmpty(config.ApiKey)))
            .ToList();

        if (includeProviderIds != null && includeProviderIds.Count > 0)
        {
            var includeSet = includeProviderIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            activeConfigs = activeConfigs
                .Where(config => includeSet.Contains(config.ProviderId))
                .ToList();
        }

        return new ProviderRefreshConfigSelection(activeConfigs);
    }
}
