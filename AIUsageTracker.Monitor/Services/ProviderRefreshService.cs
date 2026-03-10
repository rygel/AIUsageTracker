// <copyright file="ProviderRefreshService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Services;
using AIUsageTracker.Infrastructure.Configuration;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Infrastructure.Services;
using AIUsageTracker.Monitor.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Monitor.Services;

public class ProviderRefreshService : BackgroundService
{
    private const string ManualRefreshJobName = "manual-provider-refresh";
    private const string ScheduledRefreshJobName = "scheduled-provider-refresh";
    private const string StartupSeedingJobName = "startup-provider-seeding";
    private const string StartupTargetedRefreshJobName = "startup-targeted-provider-refresh";
    private const string ScheduledRefreshCoalesceKey = "scheduled-provider-refresh";
    private const string StartupSeedingCoalesceKey = "startup-provider-seeding";
    private const string StartupTargetedRefreshCoalesceKey = "startup-targeted-provider-refresh";

    private readonly ILogger<ProviderRefreshService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IUsageDatabase _database;
    private readonly INotificationService _notificationService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfigService _configService;
    private readonly IAppPathProvider _pathProvider;
    private readonly IEnumerable<IProviderService> _providers;
    private readonly UsageAlertsService _usageAlertsService;
    private readonly ProviderRefreshCircuitBreakerService _providerCircuitBreakerService;
    private readonly ProviderRefreshConfigSelector _configSelector;
    private readonly ProviderRefreshTelemetryManager _refreshTelemetryManager = new();
    private readonly ProviderUsagePersistenceService _usagePersistenceService;
    private readonly ProviderConnectivityCheckService _connectivityCheckService;
    private readonly IMonitorJobScheduler _jobScheduler;
    private readonly IProviderUsageProcessingPipeline _usageProcessingPipeline;
    private readonly IHubContext<UsageHub>? _hubContext;
    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);
    private readonly TimeSpan _refreshInterval = TimeSpan.FromMinutes(5);
    private static bool _debugMode = false;
    private ProviderManager? _providerManager;
    private int _maxConcurrentProviderRequests = ProviderManager.DefaultMaxConcurrentProviderRequests;
    private static readonly ActivitySource ActivitySource = MonitorActivitySources.Refresh;

    public static void SetDebugMode(bool debug)
    {
        _debugMode = debug;
    }

    public ProviderRefreshService(
        ILogger<ProviderRefreshService> logger,
        ILoggerFactory loggerFactory,
        IUsageDatabase database,
        INotificationService notificationService,
        IHttpClientFactory httpClientFactory,
        IConfigService configService,
        IAppPathProvider pathProvider,
        IEnumerable<IProviderService> providers,
        UsageAlertsService usageAlertsService,
        ProviderRefreshCircuitBreakerService providerCircuitBreakerService,
        IMonitorJobScheduler jobScheduler,
        IProviderUsageProcessingPipeline? usageProcessingPipeline = null,
        IHubContext<UsageHub>? hubContext = null)
    {
        this._logger = logger;
        this._loggerFactory = loggerFactory;
        this._database = database;
        this._notificationService = notificationService;
        this._httpClientFactory = httpClientFactory;
        this._configService = configService;
        this._pathProvider = pathProvider;
        this._providers = providers;
        this._usageAlertsService = usageAlertsService;
        this._providerCircuitBreakerService = providerCircuitBreakerService;
        this._configSelector = new ProviderRefreshConfigSelector(
            providers,
            loggerFactory.CreateLogger<ProviderRefreshConfigSelector>());
        this._usagePersistenceService = new ProviderUsagePersistenceService(
            database,
            loggerFactory.CreateLogger<ProviderUsagePersistenceService>());
        this._jobScheduler = jobScheduler;
        this._usageProcessingPipeline = usageProcessingPipeline ??
            new ProviderUsageProcessingPipeline(this._loggerFactory.CreateLogger<ProviderUsageProcessingPipeline>());
        this._connectivityCheckService = new ProviderConnectivityCheckService(
            configService,
            this._usageProcessingPipeline);
        this._hubContext = hubContext;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this._logger.LogInformation("Starting...");

        this._notificationService.Initialize();
        var initialConcurrency = await this.GetConfiguredMaxConcurrentProviderRequestsAsync().ConfigureAwait(false);
        this.InitializeProviders(initialConcurrency);

        this._jobScheduler.RegisterRecurringJob(
            ScheduledRefreshJobName,
            this._refreshInterval,
            _ => this.TriggerRefreshAsync(),
            MonitorJobPriority.Low,
            initialDelay: this._refreshInterval,
            coalesceKey: ScheduledRefreshCoalesceKey);

        var isEmpty = await this._database.IsHistoryEmptyAsync().ConfigureAwait(false);
        if (isEmpty)
        {
            this.QueueInitialDataSeeding();
        }
        else
        {
            // Database has existing data — serve it immediately WITHOUT refreshing all providers.
            // Do NOT hammer 3rd party APIs on startup. The scheduled interval will refresh on time.
            this._logger.LogInformation("Startup: serving cached data from database (next refresh in {Minutes}m).", this._refreshInterval.TotalMinutes);

            // Only do targeted refresh for system providers that need immediate correctness
            // All other providers will be refreshed on the normal scheduled interval
            this.QueueStartupTargetedRefresh();
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

    public bool QueueManualRefresh(
        bool forceAll = false,
        IReadOnlyCollection<string>? includeProviderIds = null,
        bool bypassCircuitBreaker = false)
    {
        return this._jobScheduler.Enqueue(
            ManualRefreshJobName,
            _ => this.TriggerRefreshAsync(forceAll, includeProviderIds, bypassCircuitBreaker),
            MonitorJobPriority.High);
    }

    private void QueueInitialDataSeeding()
    {
        var queued = this._jobScheduler.Enqueue(
            StartupSeedingJobName,
            async _ =>
        {
            try
            {
                this._logger.LogInformation("First-time startup: scanning for keys and seeding database.");
                await this._configService.ScanForKeysAsync().ConfigureAwait(false);
                await this.TriggerRefreshAsync(forceAll: true).ConfigureAwait(false);
                this._logger.LogInformation("First-time data seeding complete.");
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "Error during first-time data seeding.");
            }
        },
            MonitorJobPriority.High,
            coalesceKey: StartupSeedingCoalesceKey);

        if (!queued)
        {
            this._logger.LogDebug("Startup seeding job was already queued.");
        }
    }

    private void QueueStartupTargetedRefresh()
    {
        var queued = this._jobScheduler.Enqueue(
            StartupTargetedRefreshJobName,
            async _ =>
        {
            try
            {
                this._logger.LogDebug("Startup: running targeted refresh for system providers...");
                await this.TriggerRefreshAsync(
                    forceAll: true,
                    includeProviderIds: ProviderMetadataCatalog.GetStartupRefreshProviderIds()).ConfigureAwait(false);
                this._logger.LogDebug("Startup: targeted refresh complete.");
            }
            catch (Exception ex)
            {
                this._logger.LogWarning(ex, "Startup targeted refresh failed");
            }
        },
            MonitorJobPriority.Low,
            coalesceKey: StartupTargetedRefreshCoalesceKey);

        if (!queued)
        {
            this._logger.LogDebug("Startup targeted refresh job was already queued.");
        }
    }

    private async Task<int> GetConfiguredMaxConcurrentProviderRequestsAsync()
    {
        var preferences = await this._configService.GetPreferencesAsync().ConfigureAwait(false);
        return ProviderManager.ClampMaxConcurrentProviderRequests(preferences.MaxConcurrentProviderRequests);
    }

    private async Task EnsureProviderManagerConcurrencyAsync()
    {
        var configuredConcurrency = await this.GetConfiguredMaxConcurrentProviderRequestsAsync().ConfigureAwait(false);
        if (configuredConcurrency == this._maxConcurrentProviderRequests)
        {
            return;
        }

        this._logger.LogInformation(
            "Updating provider request concurrency limit from {Previous} to {Current}.",
            this._maxConcurrentProviderRequests,
            configuredConcurrency);
        this.InitializeProviders(configuredConcurrency);
    }

    private void InitializeProviders(int maxConcurrentProviderRequests)
    {
        this._logger.LogDebug("Initializing providers...");

        var configLoader = new JsonConfigLoader(
            this._loggerFactory.CreateLogger<JsonConfigLoader>(),
            this._loggerFactory.CreateLogger<TokenDiscoveryService>(),
            this._pathProvider);

        var providerList = this._providers.ToList();
        var newProviderManager = new ProviderManager(
            providerList,
            configLoader,
            this._loggerFactory.CreateLogger<ProviderManager>(),
            maxConcurrentProviderRequests);
        var previousProviderManager = this._providerManager;

        this._providerManager = newProviderManager;
        this._maxConcurrentProviderRequests = maxConcurrentProviderRequests;
        previousProviderManager?.Dispose();

        this._logger.LogDebug(
            "Initialized {Count} providers at max concurrency {MaxConcurrency}: {Providers}",
            providerList.Count,
            maxConcurrentProviderRequests,
            string.Join(", ", providerList.Select(p => p.ProviderId)));

        this._logger.LogInformation("Loaded {Count} providers", providerList.Count);
    }

    public virtual async Task TriggerRefreshAsync(
        bool forceAll = false,
        IReadOnlyCollection<string>? includeProviderIds = null,
        bool bypassCircuitBreaker = false)
    {
        using var refreshActivity = ActivitySource.StartActivity("monitor.provider_refresh", ActivityKind.Internal);
        refreshActivity?.SetTag("refresh.force_all", forceAll);
        refreshActivity?.SetTag("refresh.bypass_circuit_breaker", bypassCircuitBreaker);
        refreshActivity?.SetTag("refresh.include_provider_ids.count", includeProviderIds?.Count ?? 0);

        var refreshStopwatch = Stopwatch.StartNew();
        var refreshSucceeded = false;
        string? refreshError = null;
        this._refreshTelemetryManager.RecordRefreshAttemptStarted(DateTime.UtcNow);

        if (this._providerManager == null)
        {
            this._logger.LogWarning("ProviderManager not ready");
            this._refreshTelemetryManager.RecordRefreshTelemetry(refreshStopwatch.Elapsed, false, "ProviderManager not ready");
            refreshActivity?.SetStatus(ActivityStatusCode.Error, "ProviderManager not ready");
            return;
        }

        await this._refreshSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            await this.EnsureProviderManagerConcurrencyAsync().ConfigureAwait(false);
            if (this._providerManager == null)
            {
                this._logger.LogWarning("ProviderManager not ready");
                this._refreshTelemetryManager.RecordRefreshTelemetry(refreshStopwatch.Elapsed, false, "ProviderManager not ready");
                refreshActivity?.SetStatus(ActivityStatusCode.Error, "ProviderManager not ready");
                return;
            }

            this._logger.LogDebug("Starting data refresh - {Time}", DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));

            if (this._hubContext != null)
            {
                await this._hubContext.Clients.All.SendAsync("RefreshStarted").ConfigureAwait(false);
            }

            this._logger.LogInformation("Refreshing...");
            var (configs, activeConfigs) = await this.LoadConfigsForRefreshAsync(forceAll, includeProviderIds).ConfigureAwait(false);
            refreshActivity?.SetTag("refresh.configs.total", configs.Count);
            refreshActivity?.SetTag("refresh.configs.active", activeConfigs.Count);
            await this.PersistConfiguredProvidersAsync(configs).ConfigureAwait(false);

            var refreshableConfigs = bypassCircuitBreaker
                ? activeConfigs
                : this._providerCircuitBreakerService.GetRefreshableConfigs(activeConfigs, forceAll);
            var circuitSkippedCount = activeConfigs.Count - refreshableConfigs.Count;
            refreshActivity?.SetTag("refresh.configs.refreshable", refreshableConfigs.Count);
            refreshActivity?.SetTag("refresh.configs.skipped_by_circuit", circuitSkippedCount);
            if (circuitSkippedCount > 0)
            {
                this._logger.LogInformation("Circuit breaker skipping {Count} provider(s) this cycle", circuitSkippedCount);
            }

            if (refreshableConfigs.Count > 0)
            {
                await this.RefreshAndStoreProviderDataAsync(configs, refreshableConfigs).ConfigureAwait(false);
            }
            else
            {
                this._logger.LogDebug("No refreshable providers currently available.");
                this._logger.LogInformation("No providers configured or all providers are in backoff.");
            }

            await this._database.CleanupOldSnapshotsAsync().ConfigureAwait(false);
            await this._database.OptimizeAsync().ConfigureAwait(false);
            this._logger.LogInformation("Cleanup complete");
            refreshSucceeded = true;
            refreshActivity?.SetStatus(ActivityStatusCode.Ok);

            if (this._hubContext != null)
            {
                await this._hubContext.Clients.All.SendAsync("UsageUpdated").ConfigureAwait(false);
            }
        }
        catch (Exception ex)
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
        }
    }

    private async Task<(List<ProviderConfig> AllConfigs, List<ProviderConfig> ActiveConfigs)> LoadConfigsForRefreshAsync(
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

    private async Task PersistConfiguredProvidersAsync(IEnumerable<ProviderConfig> configs)
    {
        foreach (var config in configs)
        {
            await this._database.StoreProviderAsync(config).ConfigureAwait(false);
        }
    }

    private async Task RefreshAndStoreProviderDataAsync(List<ProviderConfig> allConfigs, List<ProviderConfig> refreshableConfigs)
    {
        if (this._providerManager == null)
        {
            throw new InvalidOperationException("ProviderManager not initialized");
        }

        this._logger.LogDebug("Querying {Count} providers with API keys...", refreshableConfigs.Count);
        this._logger.LogInformation("Querying {Count} providers", refreshableConfigs.Count);

        var providerIdsToQuery = refreshableConfigs
            .Select(c => c.ProviderId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var usages = await this._providerManager.GetAllUsageAsync(
            forceRefresh: true,
            progressCallback: _ => { },
            includeProviderIds: providerIdsToQuery).ConfigureAwait(false);

        this._logger.LogDebug("Received {Count} total usage results", usages.Count());

        var activeProviderIds = refreshableConfigs
            .Select(c => c.ProviderId)
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
                ? $"{usage.RequestsPercentage:F1}% used"
                : usage.Description;
            this._logger.LogDebug("  {ProviderId}: [{Status}] {Message}", usage.ProviderId, status, message);
        }

        this._providerCircuitBreakerService.UpdateProviderFailureStates(refreshableConfigs, filteredUsages);
        await this._usagePersistenceService
            .PersistUsageAndDynamicProvidersAsync(filteredUsages, activeProviderIds)
            .ConfigureAwait(false);

        await this._usageAlertsService.DetectResetEventsAsync(filteredUsages).ConfigureAwait(false);
        this._usageAlertsService.CheckUsageAlerts(filteredUsages, prefs, allConfigs);

        this._logger.LogInformation("Done: {Count} records", filteredUsages.Count);
        this._logger.LogDebug("Refresh complete. Stored {Count} provider histories", filteredUsages.Count);
    }

    public RefreshTelemetrySnapshot GetRefreshTelemetrySnapshot()
    {
        return this._refreshTelemetryManager.GetSnapshot(this._providerCircuitBreakerService.GetProviderDiagnostics());
    }

    public async Task<(bool success, string message, int status)> CheckProviderAsync(string providerId)
    {
        if (this._providerManager == null)
        {
            return (false, "ProviderManager not initialized", 503);
        }

        try
        {
            await this._refreshSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                await this.EnsureProviderManagerConcurrencyAsync().ConfigureAwait(false);
                if (this._providerManager == null)
                {
                    return (false, "ProviderManager not initialized", 503);
                }

                var usages = await this._providerManager.GetUsageAsync(providerId).ConfigureAwait(false);
                return await this._connectivityCheckService.EvaluateAsync(providerId, usages).ConfigureAwait(false);
            }
            finally
            {
                this._refreshSemaphore.Release();
            }
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Provider connectivity check failed for {ProviderId}", providerId);
            return (false, ex.Message, 500);
        }
    }
}
