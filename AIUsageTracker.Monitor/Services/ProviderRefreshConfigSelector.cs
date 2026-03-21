// <copyright file="ProviderRefreshConfigSelector.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Monitor.Services;

internal sealed class ProviderRefreshConfigSelector
{
    private readonly HashSet<string> _autoIncludedProviderIds;
    private readonly ILogger<ProviderRefreshConfigSelector> _logger;

    public ProviderRefreshConfigSelector(
        IEnumerable<IProviderService> providers,
        ILogger<ProviderRefreshConfigSelector> logger)
    {
        this._autoIncludedProviderIds = providers
            .Select(provider => provider.ProviderId)
            .Where(id => ProviderMetadataCatalog.Find(id)?.AutoIncludeWhenUnconfigured ?? false)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        this._logger = logger;
    }

    public void EnsureAutoIncludedConfigs(List<ProviderConfig> configs)
    {
        var configuredProviderIds = configs
            .Select(config => config.ProviderId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var providerId in this._autoIncludedProviderIds)
        {
            if (configuredProviderIds.Contains(providerId))
            {
                continue;
            }

            if (!ProviderMetadataCatalog.TryCreateDefaultConfig(providerId, out var config))
            {
                this._logger.LogWarning(
                    "Failed to create default config for auto-included provider {ProviderId}.",
                    providerId);
                continue;
            }

            configs.Add(config);
            configuredProviderIds.Add(config.ProviderId);
        }
    }

    public ProviderRefreshConfigSelection SelectActiveConfigs(
        IReadOnlyCollection<ProviderConfig> configs,
        bool forceAll,
        IReadOnlyCollection<string>? includeProviderIds)
    {
        var activeConfigs = configs
            .Where(config =>
                ProviderMetadataCatalog.ShouldPersistProviderId(config.ProviderId) &&
                (forceAll ||
                 this.IsAutoIncludedProviderConfig(config.ProviderId) ||
                 !string.IsNullOrEmpty(config.ApiKey)))
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

    private bool IsAutoIncludedProviderConfig(string providerId)
    {
        return (ProviderMetadataCatalog.Find(providerId)?.AutoIncludeWhenUnconfigured ?? false) &&
               this._autoIncludedProviderIds.Contains(ProviderMetadataCatalog.GetCanonicalProviderId(providerId));
    }
}
