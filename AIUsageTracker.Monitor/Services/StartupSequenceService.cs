// <copyright file="StartupSequenceService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Monitor.Services;

public sealed class StartupSequenceService
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

    public void QueueInitialDataSeeding(Func<CancellationToken, Task> refreshAllAsync)
    {
        this._refreshJobScheduler.QueueInitialDataSeeding(ct => this.RunStartupSeedingAsync(refreshAllAsync, ct));
    }

    public void QueueStartupTargetedRefresh(Func<IReadOnlyCollection<string>, CancellationToken, Task> targetedRefreshAsync)
    {
        this._refreshJobScheduler.QueueStartupTargetedRefresh(
            ct => this.RunStartupTargetedRefreshAsync(targetedRefreshAsync, ct));
    }

    private async Task RunStartupSeedingAsync(Func<CancellationToken, Task> refreshAllAsync, CancellationToken cancellationToken)
    {
        try
        {
            this._logger.LogInformation("First-time startup: scanning for keys and seeding database.");
            await this._configService.ScanForKeysAsync().ConfigureAwait(false);
            await refreshAllAsync(cancellationToken).ConfigureAwait(false);
            this._logger.LogInformation("First-time data seeding complete.");
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            this._logger.LogInformation(ex, "Startup seeding cancelled due to shutdown.");
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            this._logger.LogError(ex, "Error during first-time data seeding.");
            MonitorInfoPersistence.ReportError($"Startup seeding failed: {ex.Message}", this._pathProvider, this._logger);
        }
    }

    private async Task RunStartupTargetedRefreshAsync(Func<IReadOnlyCollection<string>, CancellationToken, Task> targetedRefreshAsync, CancellationToken cancellationToken)
    {
        try
        {
            this._logger.LogDebug("Startup: running targeted refresh for system providers...");
            await targetedRefreshAsync(ProviderMetadataCatalog.GetStartupRefreshProviderIds(), cancellationToken).ConfigureAwait(false);
            this._logger.LogDebug("Startup: targeted refresh complete.");
        }
        catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
        {
            this._logger.LogInformation(ex, "Startup targeted refresh cancelled due to shutdown.");
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            this._logger.LogWarning(ex, "Startup targeted refresh failed");
            MonitorInfoPersistence.ReportError($"Startup targeted refresh failed: {ex.Message}", this._pathProvider, this._logger);
        }
    }
}
