// <copyright file="StartupSequenceService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Monitor.Services;

internal sealed class StartupSequenceService
{
    private readonly ProviderRefreshJobScheduler _refreshJobScheduler;
    private readonly IConfigService _configService;
    private readonly IAppPathProvider _pathProvider;
    private readonly ILogger<StartupSequenceService> _logger;

    public StartupSequenceService(
        ProviderRefreshJobScheduler refreshJobScheduler,
        IConfigService configService,
        IAppPathProvider pathProvider,
        ILogger<StartupSequenceService> logger)
    {
        this._refreshJobScheduler = refreshJobScheduler;
        this._configService = configService;
        this._pathProvider = pathProvider;
        this._logger = logger;
    }

    public void QueueInitialDataSeeding(Func<Task> refreshAllAsync)
    {
        this._refreshJobScheduler.QueueInitialDataSeeding(_ => this.RunStartupSeedingAsync(refreshAllAsync));
    }

    public void QueueStartupTargetedRefresh(Func<IReadOnlyCollection<string>, Task> targetedRefreshAsync)
    {
        this._refreshJobScheduler.QueueStartupTargetedRefresh(
            _ => this.RunStartupTargetedRefreshAsync(targetedRefreshAsync));
    }

    private async Task RunStartupSeedingAsync(Func<Task> refreshAllAsync)
    {
        try
        {
            this._logger.LogInformation("First-time startup: scanning for keys and seeding database.");
            await this._configService.ScanForKeysAsync().ConfigureAwait(false);
            await refreshAllAsync().ConfigureAwait(false);
            this._logger.LogInformation("First-time data seeding complete.");
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error during first-time data seeding.");
            MonitorInfoPersistence.ReportError($"Startup seeding failed: {ex.Message}", this._pathProvider, this._logger);
        }
    }

    private async Task RunStartupTargetedRefreshAsync(Func<IReadOnlyCollection<string>, Task> targetedRefreshAsync)
    {
        try
        {
            this._logger.LogDebug("Startup: running targeted refresh for system providers...");
            await targetedRefreshAsync(ProviderMetadataCatalog.GetStartupRefreshProviderIds()).ConfigureAwait(false);
            this._logger.LogDebug("Startup: targeted refresh complete.");
        }
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "Startup targeted refresh failed");
            MonitorInfoPersistence.ReportError($"Startup targeted refresh failed: {ex.Message}", this._pathProvider, this._logger);
        }
    }
}
