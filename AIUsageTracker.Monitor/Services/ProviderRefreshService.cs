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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Monitor.Services;

public class ProviderRefreshService : BackgroundService
{
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
    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);
    private readonly TimeSpan _refreshInterval = TimeSpan.FromMinutes(5);
    private static bool _debugMode = false;
    private ProviderManager? _providerManager;
    private long _refreshCount;
    private long _refreshFailureCount;
    private long _refreshTotalLatencyMs;
    private long _lastRefreshLatencyMs;
    private readonly object _telemetryLock = new();
    private DateTime? _lastRefreshCompletedUtc;
    private string? _lastRefreshError;

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
        ProviderRefreshCircuitBreakerService providerCircuitBreakerService)
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
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        this._logger.LogInformation("Starting...");

        this._notificationService.Initialize();
        this.InitializeProviders();

        var isEmpty = await this._database.IsHistoryEmptyAsync().ConfigureAwait(false);
        if (isEmpty)
        {
            this.StartInitialDataSeeding(stoppingToken);
        }
        else
        {
            // Database has existing data — serve it immediately WITHOUT refreshing all providers.
            // Do NOT hammer 3rd party APIs on startup. The scheduled interval will refresh on time.
            this._logger.LogInformation("Startup: serving cached data from database (next refresh in {Minutes}m).", this._refreshInterval.TotalMinutes);

            // Only do targeted refresh for system providers that need immediate correctness
            // All other providers will be refreshed on the normal scheduled interval
            this.StartStartupTargetedRefresh(stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                this._logger.LogDebug("Next refresh in {Minutes} minutes...", this._refreshInterval.TotalMinutes);
                await Task.Delay(this._refreshInterval, stoppingToken).ConfigureAwait(false);
                await this.TriggerRefreshAsync().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "Error during scheduled refresh: {Message}", ex.Message);
            }
        }

        this._logger.LogInformation("Stopping...");
    }

    private void StartInitialDataSeeding(CancellationToken stoppingToken)
    {
        // First-time startup: scan for keys and populate the database.
        // Fire as a background task so the HTTP server starts serving immediately.
        // The Slim UI's rapid-poll will pick up the data once the refresh completes.
        _ = Task.Run(
            async () =>
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
        }, stoppingToken);
    }

    private void StartStartupTargetedRefresh(CancellationToken stoppingToken)
    {
        _ = Task.Run(
            async () =>
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
        }, stoppingToken);
    }

    private void InitializeProviders()
    {
        this._logger.LogDebug("Initializing providers...");

        var configLoader = new JsonConfigLoader(
            this._loggerFactory.CreateLogger<JsonConfigLoader>(),
            this._loggerFactory.CreateLogger<TokenDiscoveryService>(),
            this._pathProvider);

        var providerList = this._providers.ToList();

        this._providerManager = new ProviderManager(
            providerList,
            configLoader,
            this._loggerFactory.CreateLogger<ProviderManager>());

        this._logger.LogDebug(
            "Initialized {Count} providers: {Providers}",
            providerList.Count, string.Join(", ", providerList.Select(p => p.ProviderId)));

        this._logger.LogInformation("Loaded {Count} providers", providerList.Count);
    }

    public virtual async Task TriggerRefreshAsync(
        bool forceAll = false,
        IReadOnlyCollection<string>? includeProviderIds = null,
        bool bypassCircuitBreaker = false)
    {
        var refreshStopwatch = Stopwatch.StartNew();
        var refreshSucceeded = false;
        string? refreshError = null;

        if (this._providerManager == null)
        {
            this._logger.LogWarning("ProviderManager not ready");
            this.RecordRefreshTelemetry(refreshStopwatch.Elapsed, false, "ProviderManager not ready");
            return;
        }

        await this._refreshSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            this._logger.LogDebug("Starting data refresh - {Time}", DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));

            this._logger.LogInformation("Refreshing...");
            var (configs, activeConfigs) = await this.LoadConfigsForRefreshAsync(forceAll, includeProviderIds).ConfigureAwait(false);
            await this.PersistConfiguredProvidersAsync(configs).ConfigureAwait(false);

            var refreshableConfigs = bypassCircuitBreaker
                ? activeConfigs
                : this._providerCircuitBreakerService.GetRefreshableConfigs(activeConfigs, forceAll);
            var circuitSkippedCount = activeConfigs.Count - refreshableConfigs.Count;
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
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Refresh failed: {Message}", ex.Message);
            MonitorInfoPersistence.ReportError($"Refresh failed: {ex.Message}", this._pathProvider, this._logger);
            refreshError = ex.Message;
        }
        finally
        {
            refreshStopwatch.Stop();
            this.RecordRefreshTelemetry(refreshStopwatch.Elapsed, refreshSucceeded, refreshError);
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

        this.EnsureAutoIncludedConfigs(configs);

        var activeConfigs = configs
            .Where(c =>
                forceAll ||
                this.IsAutoIncludedProviderConfig(c.ProviderId) ||
                !string.IsNullOrEmpty(c.ApiKey))
            .ToList();

        if (activeConfigs.Any(config => ProviderMetadataCatalog.ShouldSuppressConfig(activeConfigs, config)))
        {
            var beforeCount = activeConfigs.Count;
            activeConfigs = activeConfigs
                .Where(c => !ProviderMetadataCatalog.ShouldSuppressConfig(activeConfigs, c))
                .ToList();
            this._logger.LogInformation(
                "Suppressed duplicate session-backed provider while canonical provider is active (removed {Count}).",
                beforeCount - activeConfigs.Count);
        }

        if (includeProviderIds != null && includeProviderIds.Count > 0)
        {
            var includeSet = includeProviderIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            activeConfigs = activeConfigs
                .Where(c => includeSet.Contains(c.ProviderId))
                .ToList();
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

        var validatedUsages = this.ValidateDetailContract(usages).ToList();
        this._logger.LogDebug("Validated {Count} usage results after detail contract check", validatedUsages.Count);

        var activeProviderIds = refreshableConfigs
            .Select(c => c.ProviderId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filteredUsages = this.FilterUsagesForActiveProviders(validatedUsages, activeProviderIds);

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
        await this.UpsertDynamicProvidersAsync(filteredUsages, activeProviderIds).ConfigureAwait(false);
        await this.StoreUsageHistoryAndSnapshotsAsync(filteredUsages).ConfigureAwait(false);

        await this._usageAlertsService.DetectResetEventsAsync(filteredUsages).ConfigureAwait(false);
        var prefs = await this._configService.GetPreferencesAsync().ConfigureAwait(false);
        this._usageAlertsService.CheckUsageAlerts(filteredUsages, prefs, allConfigs);

        this._logger.LogInformation("Done: {Count} records", filteredUsages.Count);
        this._logger.LogDebug("Refresh complete. Stored {Count} provider histories", filteredUsages.Count);
    }

    private List<ProviderUsage> FilterUsagesForActiveProviders(
        IReadOnlyCollection<ProviderUsage> validatedUsages,
        HashSet<string> activeProviderIds)
    {
        return validatedUsages
            .Where(u =>
                IsUsageForAnyActiveProvider(activeProviderIds, u.ProviderId) &&
                !IsPlaceholderUnavailableUsage(u))
            .ToList();
    }

    private async Task UpsertDynamicProvidersAsync(List<ProviderUsage> filteredUsages, HashSet<string> activeProviderIds)
    {
        foreach (var usage in filteredUsages)
        {
            var isKnownActiveProvider = activeProviderIds.Contains(usage.ProviderId);
            if (!IsDynamicChildOfAnyActiveProvider(activeProviderIds, usage.ProviderId) &&
                isKnownActiveProvider)
            {
                continue;
            }

            if (!isKnownActiveProvider)
            {
                this._logger.LogInformation("Auto-registering dynamic provider: {ProviderId}", usage.ProviderId);
            }

            var dynamicConfig = new ProviderConfig
            {
                ProviderId = usage.ProviderId,
                Type = usage.IsQuotaBased ? "quota-based" : "pay-as-you-go",
                AuthSource = usage.AuthSource,
                ApiKey = "dynamic", // Placeholder to mark as active
            };

            await this._database.StoreProviderAsync(dynamicConfig, usage.ProviderName).ConfigureAwait(false);
            if (!isKnownActiveProvider)
            {
                activeProviderIds.Add(usage.ProviderId);
            }
        }
    }

    private async Task StoreUsageHistoryAndSnapshotsAsync(List<ProviderUsage> filteredUsages)
    {
        await this._database.StoreHistoryAsync(filteredUsages).ConfigureAwait(false);
        this._logger.LogDebug("Stored {Count} provider histories", filteredUsages.Count);

        foreach (var usage in filteredUsages.Where(u => !string.IsNullOrEmpty(u.RawJson)))
        {
            await this._database.StoreRawSnapshotAsync(usage.ProviderId, usage.RawJson!, usage.HttpStatus).ConfigureAwait(false);
        }
    }

    private static bool IsPlaceholderUnavailableUsage(ProviderUsage usage)
    {
        return usage.RequestsAvailable == 0 &&
               usage.RequestsUsed == 0 &&
               usage.RequestsPercentage == 0 &&
               !usage.IsAvailable;
    }

    public RefreshTelemetrySnapshot GetRefreshTelemetrySnapshot()
    {
        var refreshCount = Interlocked.Read(ref this._refreshCount);
        var refreshFailureCount = Interlocked.Read(ref this._refreshFailureCount);
        var refreshTotalLatencyMs = Interlocked.Read(ref this._refreshTotalLatencyMs);
        var lastRefreshLatencyMs = Interlocked.Read(ref this._lastRefreshLatencyMs);

        DateTime? lastRefreshCompletedUtc;
        string? lastRefreshError;
        lock (this._telemetryLock)
        {
            lastRefreshCompletedUtc = this._lastRefreshCompletedUtc;
            lastRefreshError = this._lastRefreshError;
        }

        var refreshSuccessCount = Math.Max(0, refreshCount - refreshFailureCount);
        var averageLatencyMs = refreshCount == 0 ? 0 : refreshTotalLatencyMs / (double)refreshCount;
        var errorRatePercent = refreshCount == 0 ? 0 : (refreshFailureCount / (double)refreshCount) * 100.0;

        return new RefreshTelemetrySnapshot
        {
            RefreshCount = refreshCount,
            RefreshSuccessCount = refreshSuccessCount,
            RefreshFailureCount = refreshFailureCount,
            ErrorRatePercent = errorRatePercent,
            AverageLatencyMs = averageLatencyMs,
            LastLatencyMs = lastRefreshLatencyMs,
            LastRefreshCompletedUtc = lastRefreshCompletedUtc,
            LastError = lastRefreshError,
        };
    }

    private void RecordRefreshTelemetry(TimeSpan duration, bool success, string? errorMessage)
    {
        var latencyMs = (long)Math.Max(0, duration.TotalMilliseconds);

        Interlocked.Increment(ref this._refreshCount);

        Interlocked.Add(ref this._refreshTotalLatencyMs, latencyMs);

        Interlocked.Exchange(ref this._lastRefreshLatencyMs, latencyMs);

        if (!success)
        {
            Interlocked.Increment(ref this._refreshFailureCount);
        }

        lock (this._telemetryLock)
        {
            this._lastRefreshCompletedUtc = DateTime.UtcNow;
            this._lastRefreshError = success ? null : errorMessage;
        }
    }

    private void EnsureAutoIncludedConfigs(List<ProviderConfig> configs)
    {
        var configuredProviderIds = configs
            .Select(config => config.ProviderId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in this._providers
                     .Select(provider => provider.Definition)
                     .Where(definition => definition.AutoIncludeWhenUnconfigured))
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

    private bool IsAutoIncludedProviderConfig(string providerId)
    {
        return this._providers.Any(provider =>
            provider.Definition.AutoIncludeWhenUnconfigured &&
            provider.Definition.HandlesProviderId(providerId));
    }

    private static bool IsUsageForAnyActiveProvider(HashSet<string> activeProviderIds, string usageProviderId)
    {
        return activeProviderIds.Any(providerId => IsUsageForProvider(providerId, usageProviderId));
    }

    private static bool IsUsageForProvider(string providerId, string usageProviderId)
    {
        if (usageProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return usageProviderId.StartsWith($"{providerId}.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDynamicChildOfAnyActiveProvider(HashSet<string> activeProviderIds, string usageProviderId)
    {
        return activeProviderIds.Any(providerId =>
            usageProviderId.StartsWith($"{providerId}.", StringComparison.OrdinalIgnoreCase));
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
                var usages = await this._providerManager.GetUsageAsync(providerId).ConfigureAwait(false);
                var usage = usages.FirstOrDefault();

                if (usage == null)
                {
                    return (false, "No usage data returned", 404);
                }

                if (usage.HttpStatus >= 400 && usage.HttpStatus != 429) // 429 is rate limit, which means auth works
                {
                    return (false, usage.Description, usage.HttpStatus);
                }

                if (!usage.IsAvailable)
                {
                    return (false, usage.Description, 503);
                }

                return (true, "Connected", 200);
            }
            finally
            {
                this._refreshSemaphore.Release();
            }
        }
        catch (Exception ex)
        {
            return (false, ex.Message, 500);
        }
    }

    private IEnumerable<ProviderUsage> ValidateDetailContract(IEnumerable<ProviderUsage> usages)
    {
        foreach (var usage in usages)
        {
            var validationErrors = new List<string>();

            if (usage.Details != null)
            {
                foreach (var detail in usage.Details)
                {
                    if (string.IsNullOrWhiteSpace(detail.Name))
                    {
                        validationErrors.Add("Detail Name is empty");
                    }

                    if (detail.DetailType == ProviderUsageDetailType.Unknown)
                    {
                        validationErrors.Add($"DetailType is Unknown (must be QuotaWindow, Credit, Model, or Other)");
                    }

                    if (detail.DetailType == ProviderUsageDetailType.QuotaWindow)
                    {
                        if (detail.WindowKind == WindowKind.None)
                        {
                            validationErrors.Add("QuotaWindow details must have WindowKind set (Primary, Secondary, or Spark)");
                        }
                    }
                }
            }

            if (validationErrors.Count > 0)
            {
                this._logger.LogWarning(
                    "Provider {ProviderId} emitted invalid details: {Errors}. Marking as unavailable.",
                    usage.ProviderId,
                    string.Join("; ", validationErrors));

                yield return new ProviderUsage
                {
                    ProviderId = usage.ProviderId,
                    ProviderName = usage.ProviderName,
                    IsAvailable = false,
                    Description = $"Invalid detail contract: {string.Join("; ", validationErrors)}",
                    PlanType = usage.PlanType,
                    IsQuotaBased = usage.IsQuotaBased,
                };
            }
            else
            {
                yield return usage;
            }
        }
    }
}
