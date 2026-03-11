// <copyright file="ProviderRefreshServiceDependencies.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Monitor.Services;

internal sealed record ProviderRefreshServiceDependencies(
    ProviderRefreshConfigLoadingService ConfigLoadingService,
    ProviderUsagePersistenceService UsagePersistenceService,
    ProviderConnectivityCheckService ConnectivityCheckService,
    ProviderRefreshJobScheduler RefreshJobScheduler,
    ProviderManagerLifecycleService ProviderManagerLifecycle,
    ProviderRefreshNotificationService RefreshNotificationService,
    StartupSequenceService StartupSequenceService,
    IProviderUsageProcessingPipeline UsageProcessingPipeline);
