// <copyright file="ProviderRefreshServiceFactory.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Monitor.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Monitor.Services;

internal static class ProviderRefreshServiceFactory
{
    public static ProviderRefreshServiceDependencies CreateDependencies(
        ILoggerFactory loggerFactory,
        IUsageDatabase database,
        IConfigService configService,
        IAppPathProvider pathProvider,
        IEnumerable<IProviderService> providers,
        UsageAlertsService usageAlertsService,
        IMonitorJobScheduler jobScheduler,
        IProviderUsageProcessingPipeline? usageProcessingPipeline,
        IHubContext<UsageHub>? hubContext)
    {
        var configSelector = new ProviderRefreshConfigSelector(
            providers,
            loggerFactory.CreateLogger<ProviderRefreshConfigSelector>());
        var resolvedUsageProcessingPipeline = usageProcessingPipeline ??
            new ProviderUsageProcessingPipeline(loggerFactory.CreateLogger<ProviderUsageProcessingPipeline>());
        var refreshJobScheduler = new ProviderRefreshJobScheduler(
            jobScheduler,
            loggerFactory.CreateLogger<ProviderRefreshJobScheduler>());

        return new ProviderRefreshServiceDependencies(
            new ProviderRefreshConfigLoadingService(
                configService,
                database,
                configSelector,
                loggerFactory.CreateLogger<ProviderRefreshConfigLoadingService>()),
            new ProviderUsagePersistenceService(
                database,
                loggerFactory.CreateLogger<ProviderUsagePersistenceService>()),
            new ProviderConnectivityCheckService(
                configService,
                resolvedUsageProcessingPipeline),
            refreshJobScheduler,
            new ProviderManagerLifecycleService(
                loggerFactory.CreateLogger<ProviderManagerLifecycleService>(),
                loggerFactory,
                configService,
                pathProvider,
                providers),
            new ProviderRefreshNotificationService(
                usageAlertsService,
                hubContext),
            new StartupSequenceService(
                refreshJobScheduler,
                configService,
                loggerFactory.CreateLogger<StartupSequenceService>()),
            resolvedUsageProcessingPipeline);
    }
}
