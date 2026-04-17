// <copyright file="ProviderRefreshConfigLoadingService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Monitor.Services;

public sealed class ProviderRefreshConfigLoadingService
{
    private readonly IConfigService _configService;
    private readonly IUsageDatabase _database;
    private readonly ILogger<ProviderRefreshConfigLoadingService> _logger;

    public ProviderRefreshConfigLoadingService(
        IConfigService configService,
        IUsageDatabase database,
        ILogger<ProviderRefreshConfigLoadingService> logger)
    {
        this._configService = configService;
        this._database = database;
        this._logger = logger;
    }

    public async Task<(List<ProviderConfig> AllConfigs, List<ProviderConfig> ActiveConfigs)> LoadConfigsForRefreshAsync(
        bool forceAll,
        IReadOnlyCollection<string>? includeProviderIds)
    {
        var configsReadOnly = await this._configService.GetConfigsAsync().ConfigureAwait(false);
        var configs = configsReadOnly.ToList();
        this._logger.LogInformation("Loading {Count} provider configurations...", configs.Count);

        foreach (var config in configs)
        {
            var hasKey = !string.IsNullOrEmpty(config.ApiKey);
            this._logger.LogInformation(
                "Provider {ProviderId}: {Status}",
                config.ProviderId,
                hasKey ? $"Has API key ({config.ApiKey?.Length ?? 0} chars)" : "NO API KEY");
        }

        var selection = ProviderRefreshConfigSelector.SelectActiveConfigs(configs, forceAll, includeProviderIds);
        var activeConfigs = selection.ActiveConfigs;

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
        ArgumentNullException.ThrowIfNull(configs);

        foreach (var config in configs)
        {
            await this._database.StoreProviderAsync(config).ConfigureAwait(false);
        }
    }
}
