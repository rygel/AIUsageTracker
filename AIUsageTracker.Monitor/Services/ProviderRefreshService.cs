// <copyright file="ProviderRefreshService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Services;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Monitor.Services;

public class ProviderRefreshService : BackgroundService
{
    private readonly ILogger<ProviderRefreshService> _logger;
    private readonly IUsageDatabase _database;
    private readonly INotificationService _notificationService;
    private readonly IConfigService _configService;
    private readonly IAppPathProvider _pathProvider;
    private readonly ProviderRefreshCircuitBreakerService _providerCircuitBreakerService;
    private readonly ProviderRefreshConfigLoadingService _configLoadingService;
    private readonly ProviderRefreshTelemetryManager _refreshTelemetryManager = new();
    private readonly ProviderUsagePersistenceService _usagePersistenceService;
    private readonly ProviderConnectivityCheckService _connectivityCheckService;
    private readonly ProviderRefreshJobScheduler _refreshJobScheduler;
    private readonly ProviderManagerLifecycleService _providerManagerLifecycle;
    private readonly ProviderRefreshNotificationService _refreshNotificationService;
    private static readonly ActivitySource ActivitySource = MonitorActivitySources.Refresh;
    private readonly StartupSequenceService _startupSequenceService;
    private readonly IProviderUsageProcessingPipeline _usageProcessingPipeline;
    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);
    private volatile CancellationTokenSource? _activeRefreshCts;
    private readonly TimeSpan _refreshInterval = TimeSpan.FromMinutes(5);

    public ProviderRefreshService(
        ILogger<ProviderRefreshService> logger,
        IUsageDatabase database,
        INotificationService notificationService,
        IConfigService configService,
        IAppPathProvider pathProvider,
        ProviderRefreshCircuitBreakerService providerCircuitBreakerService,
        ProviderRefreshConfigLoadingService configLoadingService,
        ProviderUsagePersistenceService usagePersistenceService,
        ProviderConnectivityCheckService connectivityCheckService,
        ProviderRefreshJobScheduler refreshJobScheduler,
        ProviderManagerLifecycleService providerManagerLifecycle,
        ProviderRefreshNotificationService refreshNotificationService,
        StartupSequenceService startupSequenceService,
        IProviderUsageProcessingPipeline usageProcessingPipeline)
    {
        this._logger = logger;
        this._database = database;
        this._notificationService = notificationService;
        this._configService = configService;
        this._pathProvider = pathProvider;
        this._providerCircuitBreakerService = providerCircuitBreakerService;
        this._configLoadingService = configLoadingService;
        this._usagePersistenceService = usagePersistenceService;
        this._connectivityCheckService = connectivityCheckService;
        this._refreshJobScheduler = refreshJobScheduler;
        this._providerManagerLifecycle = providerManagerLifecycle;
        this._refreshNotificationService = refreshNotificationService;
        this._startupSequenceService = startupSequenceService;
        this._usageProcessingPipeline = usageProcessingPipeline;
    }

    public bool QueueManualRefresh(
        bool forceAll = false,
        IReadOnlyCollection<string>? includeProviderIds = null,
        bool bypassCircuitBreaker = false)
    {
        var coalesceKey = BuildManualRefreshCoalesceKey(forceAll, includeProviderIds, bypassCircuitBreaker);
        return this._refreshJobScheduler.QueueManualRefresh(
            ct => this.TriggerRefreshAsync(forceAll, includeProviderIds, bypassCircuitBreaker, ct),
            coalesceKey);
    }

    public bool QueueForceRefresh(
        bool forceAll = false,
        IReadOnlyCollection<string>? includeProviderIds = null)
    {
        return this.QueueManualRefresh(
            forceAll: forceAll,
            includeProviderIds: includeProviderIds,
            bypassCircuitBreaker: true);
    }

    public void CancelActiveRefresh()
    {
        var cts = this._activeRefreshCts;
        if (cts != null && !cts.IsCancellationRequested)
        {
            this._logger.LogInformation("Cancelling active refresh cycle (power state transition)");
            cts.Cancel();
        }
    }

    internal static string? BuildManualRefreshCoalesceKey(
        bool forceAll,
        IReadOnlyCollection<string>? includeProviderIds,
        bool bypassCircuitBreaker)
    {
        var normalizedIds = (includeProviderIds ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        if (!forceAll && !bypassCircuitBreaker && normalizedIds.Length == 0)
        {
            return null;
        }

        var includeSegment = normalizedIds.Length == 0
            ? "all"
            : string.Join(",", normalizedIds);
        return $"manual-provider-refresh|forceAll={forceAll}|bypass={bypassCircuitBreaker}|include={includeSegment}";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this._logger.LogInformation("Starting...");

        this._notificationService.Initialize();
        var initialConcurrency = await this.GetConfiguredMaxConcurrentProviderRequestsAsync().ConfigureAwait(false);
        this.InitializeProviders(initialConcurrency);

        this._refreshJobScheduler.RegisterRecurringRefresh(
            this._refreshInterval,
            ct => this.TriggerRefreshAsync(cancellationToken: ct));

        var isEmpty = await this._database.IsHistoryEmptyAsync().ConfigureAwait(false);
        if (isEmpty)
        {
            this._startupSequenceService.QueueInitialDataSeeding(
                ct => this.TriggerRefreshAsync(forceAll: true, cancellationToken: ct));
        }
        else
        {
            // Database has existing data — serve it immediately WITHOUT refreshing all providers.
            // Do NOT hammer 3rd party APIs on startup. The scheduled interval will refresh on time.
            this._logger.LogInformation("Startup: serving cached data from database (next refresh in {Minutes}m).", this._refreshInterval.TotalMinutes);

            // Only do targeted refresh for system providers that need immediate correctness
            // All other providers will be refreshed on the normal scheduled interval
            this._startupSequenceService.QueueStartupTargetedRefresh(
                (providerIds, ct) => this.TriggerRefreshAsync(forceAll: true, includeProviderIds: providerIds, cancellationToken: ct));
        }

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown path.
        }

        this._logger.LogInformation("Stopping...");
    }

    public virtual async Task TriggerRefreshAsync(
        bool forceAll = false,
        IReadOnlyCollection<string>? includeProviderIds = null,
        bool bypassCircuitBreaker = false,
        CancellationToken cancellationToken = default)
    {
        using var refreshCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        this._activeRefreshCts = refreshCts;
        using var refreshActivity = ActivitySource.StartActivity("monitor.provider_refresh", ActivityKind.Internal);
        refreshActivity?.SetTag("refresh.force_all", forceAll);
        refreshActivity?.SetTag("refresh.bypass_circuit_breaker", bypassCircuitBreaker);
        refreshActivity?.SetTag("refresh.include_provider_ids.count", includeProviderIds?.Count ?? 0);

        var refreshStopwatch = Stopwatch.StartNew();
        var refreshSucceeded = false;
        string? refreshError = null;
        this._refreshTelemetryManager.RecordRefreshAttemptStarted(DateTime.UtcNow);

        if (this.TryGetProviderManagerForRefresh(refreshStopwatch, refreshActivity) == null)
        {
            this._activeRefreshCts = null;
            return;
        }

        await this._refreshSemaphore.WaitAsync(refreshCts.Token).ConfigureAwait(false);
        try
        {
            await this.EnsureProviderManagerConcurrencyAsync().ConfigureAwait(false);
            var providerManager = this.TryGetProviderManagerForRefresh(refreshStopwatch, refreshActivity);
            if (providerManager == null)
            {
                return;
            }

            this._logger.LogDebug("Starting data refresh - {Time}", DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));

            await this._refreshNotificationService.NotifyRefreshStartedAsync().ConfigureAwait(false);

            this._logger.LogInformation("Refreshing...");
            var (configs, activeConfigs) = await this._configLoadingService
                .LoadConfigsForRefreshAsync(forceAll, includeProviderIds)
                .ConfigureAwait(false);
            refreshActivity?.SetTag("refresh.configs.total", configs.Count);
            refreshActivity?.SetTag("refresh.configs.active", activeConfigs.Count);
            await this._configLoadingService.PersistConfiguredProvidersAsync(configs).ConfigureAwait(false);

            var refreshableConfigs = bypassCircuitBreaker
                ? activeConfigs
                : this._providerCircuitBreakerService.GetRefreshableConfigs(activeConfigs, forceAll);
            var refreshableIdSet = refreshableConfigs
                .Select(c => c.ProviderId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var circuitSkippedConfigs = bypassCircuitBreaker
                ? (IList<ProviderConfig>)Array.Empty<ProviderConfig>()
                : activeConfigs.Where(c => !refreshableIdSet.Contains(c.ProviderId)).ToList();
            var circuitSkippedCount = circuitSkippedConfigs.Count;
            refreshActivity?.SetTag("refresh.configs.refreshable", refreshableConfigs.Count);
            refreshActivity?.SetTag("refresh.configs.skipped_by_circuit", circuitSkippedCount);
            if (circuitSkippedCount > 0)
            {
                this._logger.LogInformation("Circuit breaker skipping {Count} provider(s) this cycle", circuitSkippedCount);
            }

            IReadOnlyList<ProviderUsage>? refreshedUsages = null;
            if (refreshableConfigs.Count > 0 || circuitSkippedConfigs.Count > 0)
            {
                refreshedUsages = await this.RefreshAndStoreProviderDataAsync(
                        providerManager,
                        configs,
                        refreshableConfigs,
                        circuitSkippedConfigs,
                        refreshCts.Token)
                    .ConfigureAwait(false);
            }
            else
            {
                this._logger.LogDebug("No refreshable providers currently available.");
                this._logger.LogInformation("No providers configured.");
            }

            await this._database.CleanupOldSnapshotsAsync().ConfigureAwait(false);
            await this._database.CompactHistoryAsync().ConfigureAwait(false);
            await this._database.OptimizeAsync().ConfigureAwait(false);
            this._logger.LogInformation("Cleanup complete");
            refreshSucceeded = true;
            refreshActivity?.SetStatus(ActivityStatusCode.Ok);

            await this._refreshNotificationService.NotifyUsageUpdatedAsync(refreshedUsages).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            this._logger.LogError(ex, "Refresh failed: {Message}", ex.Message);
            MonitorInfoPersistence.ReportError($"Refresh failed: {ex.Message}", this._pathProvider, this._logger);
            refreshError = ex.Message;
            refreshActivity?.SetTag("error.type", ex.GetType().Name);
            refreshActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        }
        finally
        {
            refreshStopwatch.Stop();
            this._refreshTelemetryManager.RecordRefreshTelemetry(refreshStopwatch.Elapsed, refreshSucceeded, refreshError);
            refreshActivity?.SetTag("refresh.duration_ms", refreshStopwatch.Elapsed.TotalMilliseconds);
            this._refreshSemaphore.Release();
            this._activeRefreshCts = null;
        }
    }

    private async Task<IReadOnlyList<ProviderUsage>> RefreshAndStoreProviderDataAsync(
        ProviderManager providerManager,
        IList<ProviderConfig> allConfigs,
        IList<ProviderConfig> refreshableConfigs,
        IList<ProviderConfig> circuitSkippedConfigs,
        CancellationToken cancellationToken = default)
    {
        // Fetch live usage for providers whose circuit is closed.
        IEnumerable<ProviderUsage> usages = Enumerable.Empty<ProviderUsage>();
        if (refreshableConfigs.Count > 0)
        {
            this._logger.LogDebug("Querying {Count} providers with API keys...", refreshableConfigs.Count);
            this._logger.LogInformation("Querying {Count} providers", refreshableConfigs.Count);

            var providerIdsToQuery = refreshableConfigs
                .Select(c => c.ProviderId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            usages = await providerManager.GetAllUsageAsync(
                forceRefresh: true,
                progressCallback: _ => { },
                includeProviderIds: providerIdsToQuery,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            this._logger.LogDebug("Received {Count} total usage results", usages.Count());
        }

        // Synthesize "circuit open" entries so the UI shows an actionable message
        // instead of stale cached data for providers currently in backoff.
        var circuitOpenUsages = this._providerCircuitBreakerService.CreateCircuitOpenUsages(circuitSkippedConfigs);
        if (circuitOpenUsages.Count > 0)
        {
            this._logger.LogDebug("Synthesizing {Count} circuit-open usage entries", circuitOpenUsages.Count);
            usages = usages.Concat(circuitOpenUsages);
        }

        var activeProviderIds = refreshableConfigs
            .Select(c => c.ProviderId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Include circuit-skipped providers in the active set so their synthetic
        // entries pass the pipeline authority stage.
        foreach (var skipped in circuitSkippedConfigs)
        {
            activeProviderIds.Add(skipped.ProviderId);
        }

        activeProviderIds = ProviderMetadataCatalog.ExpandAcceptedUsageProviderIds(activeProviderIds)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var prefs = await this._configService.GetPreferencesAsync().ConfigureAwait(false);

        var processingResult = this._usageProcessingPipeline.Process(
            usages,
            activeProviderIds,
            prefs.IsPrivacyMode);
        var filteredUsages = processingResult.Usages.ToList();

        this._logger.LogDebug(
            "Usage pipeline accepted {Accepted}/{Total} items (invalidIdentity={InvalidIdentity}, inactiveFiltered={InactiveFiltered}, placeholderFiltered={PlaceholderFiltered}, detailAdjusted={DetailAdjusted}, normalized={Normalized}, redacted={Redacted})",
            filteredUsages.Count,
            usages.Count(),
            processingResult.InvalidIdentityCount,
            processingResult.InactiveProviderFilteredCount,
            processingResult.PlaceholderFilteredCount,
            processingResult.DetailContractAdjustedCount,
            processingResult.NormalizedCount,
            processingResult.PrivacyRedactedCount);

        this._logger.LogDebug("Provider query results:");
        foreach (var usage in filteredUsages)
        {
            var status = usage.IsAvailable ? "OK" : "FAILED";
            var message = usage.IsAvailable
                ? $"{usage.UsedPercent:F1}% used"
                : usage.Description;
            this._logger.LogDebug("  {ProviderId}: [{Status}] {Message}", usage.ProviderId, status, message);
        }

        this._providerCircuitBreakerService.UpdateProviderFailureStates(refreshableConfigs, filteredUsages);
        await this._usagePersistenceService
            .PersistUsageAndDynamicProvidersAsync(filteredUsages, activeProviderIds)
            .ConfigureAwait(false);

        await this._refreshNotificationService
            .ProcessUsageAlertsAsync(filteredUsages, prefs, allConfigs)
            .ConfigureAwait(false);

        this._logger.LogInformation("Done: {Count} records", filteredUsages.Count);
        this._logger.LogDebug("Refresh complete. Stored {Count} provider histories", filteredUsages.Count);
        return filteredUsages;
    }

    public RefreshTelemetrySnapshot GetRefreshTelemetrySnapshot()
    {
        return this._refreshTelemetryManager.GetSnapshot(this._providerCircuitBreakerService.GetProviderDiagnostics());
    }

    public async Task<(bool Success, string Message, int Status)> CheckProviderAsync(string providerId, CancellationToken cancellationToken = default)
    {
        if (this.ProviderManager == null)
        {
            return ProviderManagerNotInitialized();
        }

        try
        {
            await this._refreshSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await this.EnsureProviderManagerConcurrencyAsync().ConfigureAwait(false);
                var providerManager = this.ProviderManager;
                if (providerManager == null)
                {
                    return ProviderManagerNotInitialized();
                }

                var usages = await providerManager.GetUsageAsync(providerId, cancellationToken).ConfigureAwait(false);
                return await this._connectivityCheckService.EvaluateAsync(providerId, usages).ConfigureAwait(false);
            }
            finally
            {
                this._refreshSemaphore.Release();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            this._logger.LogError(ex, "Provider connectivity check failed for {ProviderId}", providerId);
            return (false, ex.Message, 500);
        }
    }

    public override void Dispose()
    {
        this._providerManagerLifecycle.Dispose();
        base.Dispose();
    }

    private ProviderManager? ProviderManager => this._providerManagerLifecycle.CurrentManager;

    private static (bool Success, string Message, int Status) ProviderManagerNotInitialized()
    {
        return (false, "ProviderManager not initialized", 503);
    }

    private ProviderManager? TryGetProviderManagerForRefresh(Stopwatch refreshStopwatch, Activity? refreshActivity)
    {
        var providerManager = this.ProviderManager;
        if (providerManager != null)
        {
            return providerManager;
        }

        const string error = "ProviderManager not ready";
        this._logger.LogWarning(error);
        this._refreshTelemetryManager.RecordRefreshTelemetry(refreshStopwatch.Elapsed, false, error);
        refreshActivity?.SetStatus(ActivityStatusCode.Error, error);
        return null;
    }

    private async Task<int> GetConfiguredMaxConcurrentProviderRequestsAsync()
    {
        return await this._providerManagerLifecycle.GetConfiguredMaxConcurrentProviderRequestsAsync().ConfigureAwait(false);
    }

    private async Task EnsureProviderManagerConcurrencyAsync()
    {
        await this._providerManagerLifecycle.EnsureConcurrencyAsync().ConfigureAwait(false);
    }

    private void InitializeProviders(int maxConcurrentProviderRequests)
    {
        this._providerManagerLifecycle.Initialize(maxConcurrentProviderRequests);
    }
}
