// <copyright file="ProviderRefreshConfigLoadingService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Monitor.Services;

internal sealed class ProviderRefreshConfigLoadingService
{
    private readonly IConfigService _configService;
    private readonly IUsageDatabase _database;
    private readonly ProviderRefreshConfigSelector _configSelector;
    private readonly ILogger<ProviderRefreshConfigLoadingService> _logger;

    public ProviderRefreshConfigLoadingService(
        IConfigService configService,
        IUsageDatabase database,
        ProviderRefreshConfigSelector configSelector,
        ILogger<ProviderRefreshConfigLoadingService> logger)
    {
        this._configService = configService;
        this._database = database;
        this._configSelector = configSelector;
        this._logger = logger;
    }

    public async Task<(List<ProviderConfig> AllConfigs, List<ProviderConfig> ActiveConfigs)> LoadConfigsForRefreshAsync(
        bool forceAll,
        IReadOnlyCollection<string>? includeProviderIds)
    {
        this._logger.LogInformation("Loading provider configurations...");
        var configs = await this._configService.GetConfigsAsync().ConfigureAwait(false);
        this._logger.LogInformation("Found {Count} total configurations", configs.Count);

        foreach (var config in configs)
        {
            var hasKey = !string.IsNullOrEmpty(config.ApiKey);
            this._logger.LogInformation(
                "Provider {ProviderId}: {Status}",
                config.ProviderId,
                hasKey ? $"Has API key ({config.ApiKey?.Length ?? 0} chars)" : "NO API KEY");
        }

        this._configSelector.EnsureAutoIncludedConfigs(configs);
        var selection = this._configSelector.SelectActiveConfigs(configs, forceAll, includeProviderIds);
        var activeConfigs = selection.ActiveConfigs;

        if (selection.SuppressedConfigCount > 0)
        {
            this._logger.LogInformation(
                "Suppressed duplicate session-backed provider while canonical provider is active (removed {Count}).",
                selection.SuppressedConfigCount);
        }

        this._logger.LogInformation("Refreshing {Count} configured providers", activeConfigs.Count);
        foreach (var activeConfig in activeConfigs.OrderBy(c => c.ProviderId, StringComparer.Ordinal))
        {
            this._logger.LogDebug(
                "Active config: {ProviderId} (Key present: {HasKey})",
                activeConfig.ProviderId,
                !string.IsNullOrEmpty(activeConfig.ApiKey));
        }

        return (configs, activeConfigs);
    }

    public async Task PersistConfiguredProvidersAsync(IEnumerable<ProviderConfig> configs)
    {
        foreach (var config in configs)
        {
            await this._database.StoreProviderAsync(config).ConfigureAwait(false);
        }
    }
}
