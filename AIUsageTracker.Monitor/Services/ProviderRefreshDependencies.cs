// <copyright file="ProviderRefreshDependencies.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Monitor.Services;

/// <summary>
/// Groups the refresh-specific dependencies injected into <see cref="ProviderRefreshService"/>.
/// Reduces constructor parameter count by consolidating related services.
/// </summary>
public sealed class ProviderRefreshDependencies
{
    public ProviderRefreshDependencies(
        ProviderRefreshCircuitBreakerService circuitBreakerService,
        ProviderRefreshConfigLoadingService configLoadingService,
        ProviderUsagePersistenceService usagePersistenceService,
        ProviderConnectivityCheckService connectivityCheckService,
        ProviderRefreshJobScheduler refreshJobScheduler,
        ProviderManagerLifecycleService providerManagerLifecycle,
        ProviderRefreshNotificationService refreshNotificationService,
        StartupSequenceService startupSequenceService,
        IProviderUsageProcessingPipeline usageProcessingPipeline)
    {
        this.CircuitBreakerService = circuitBreakerService;
        this.ConfigLoadingService = configLoadingService;
        this.UsagePersistenceService = usagePersistenceService;
        this.ConnectivityCheckService = connectivityCheckService;
        this.RefreshJobScheduler = refreshJobScheduler;
        this.ProviderManagerLifecycle = providerManagerLifecycle;
        this.RefreshNotificationService = refreshNotificationService;
        this.StartupSequenceService = startupSequenceService;
        this.UsageProcessingPipeline = usageProcessingPipeline;
    }

    public ProviderRefreshCircuitBreakerService CircuitBreakerService { get; }

    public ProviderRefreshConfigLoadingService ConfigLoadingService { get; }

    public ProviderUsagePersistenceService UsagePersistenceService { get; }

    public ProviderConnectivityCheckService ConnectivityCheckService { get; }

    public ProviderRefreshJobScheduler RefreshJobScheduler { get; }

    public ProviderManagerLifecycleService ProviderManagerLifecycle { get; }

    public ProviderRefreshNotificationService RefreshNotificationService { get; }

    public StartupSequenceService StartupSequenceService { get; }

    public IProviderUsageProcessingPipeline UsageProcessingPipeline { get; }
}
