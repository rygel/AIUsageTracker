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
    private readonly IReadOnlyList<ProviderDefinition> _providerDefinitions;
    private readonly ILogger<ProviderRefreshConfigSelector> _logger;

    public ProviderRefreshConfigSelector(
        IEnumerable<IProviderService> providers,
        ILogger<ProviderRefreshConfigSelector> logger)
    {
        this._providerDefinitions = providers
            .Select(provider => provider.Definition)
            .ToArray();
        this._logger = logger;
    }

    public void EnsureAutoIncludedConfigs(List<ProviderConfig> configs)
    {
        var configuredProviderIds = configs
            .Select(config => config.ProviderId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in this._providerDefinitions.Where(definition => definition.AutoIncludeWhenUnconfigured))
        {
            if (configuredProviderIds.Contains(definition.ProviderId))
            {
                continue;
            }

            if (!ProviderMetadataCatalog.TryCreateDefaultConfig(definition.ProviderId, out var config))
            {
                this._logger.LogWarning(
                    "Failed to create default config for auto-included provider {ProviderId}.",
                    definition.ProviderId);
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
                forceAll ||
                this.IsAutoIncludedProviderConfig(config.ProviderId) ||
                !string.IsNullOrEmpty(config.ApiKey))
            .ToList();

        var suppressedConfigCount = 0;
        if (activeConfigs.Any(config => ProviderMetadataCatalog.ShouldSuppressConfig(activeConfigs, config)))
        {
            var beforeCount = activeConfigs.Count;
            activeConfigs = activeConfigs
                .Where(config => !ProviderMetadataCatalog.ShouldSuppressConfig(activeConfigs, config))
                .ToList();
            suppressedConfigCount = beforeCount - activeConfigs.Count;
        }

        if (includeProviderIds != null && includeProviderIds.Count > 0)
        {
            var includeSet = includeProviderIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            activeConfigs = activeConfigs
                .Where(config => includeSet.Contains(config.ProviderId))
                .ToList();
        }

        return new ProviderRefreshConfigSelection(activeConfigs, suppressedConfigCount);
    }

    private bool IsAutoIncludedProviderConfig(string providerId)
    {
        return this._providerDefinitions.Any(definition =>
            definition.AutoIncludeWhenUnconfigured &&
            definition.HandlesProviderId(providerId));
    }
}
