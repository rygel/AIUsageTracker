// <copyright file="ProviderRefreshConfigSelector.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Monitor.Services;

public static class ProviderRefreshConfigSelector
{
    public static ProviderRefreshConfigSelection SelectActiveConfigs(
        IReadOnlyCollection<ProviderConfig> configs,
        bool forceAll,
        IReadOnlyCollection<string>? includeProviderIds)
    {
        var activeConfigs = configs
            .Where(config =>
            {
                if (!ProviderMetadataCatalog.ShouldPersistProviderId(config.ProviderId))
                {
                    return false;
                }

                // StandardApiKey providers always require a key — polling without one can
                // only return "API Key missing", which is useless to store and poll for.
                // forceAll does not override this: there is nothing useful to fetch without a key.
                if (ProviderMetadataCatalog.Find(config.ProviderId)?.SettingsMode == ProviderSettingsMode.StandardApiKey)
                {
                    return !string.IsNullOrEmpty(config.ApiKey);
                }

                // Non-StandardApiKey providers (SessionAuth, AutoDetected, ExternalAuth) respond
                // to forceAll so that scan operations pick them up even when no key is stored.
                return forceAll || !string.IsNullOrEmpty(config.ApiKey);
            })
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
