// <copyright file="ProviderRefreshService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Monitor.Services
{
    using AIUsageTracker.Core.Interfaces;
    using AIUsageTracker.Core.Models;
    using AIUsageTracker.Core.Services;
    using AIUsageTracker.Infrastructure.Configuration;
    using AIUsageTracker.Infrastructure.Providers;
    using AIUsageTracker.Infrastructure.Services;
    using Microsoft.Extensions.Hosting;
    using Microsoft.Extensions.Logging;
    using System.Diagnostics;

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
        private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);
        private readonly TimeSpan _refreshInterval = TimeSpan.FromMinutes(5);
        private static bool _debugMode = false;
        private ProviderManager? _providerManager;
        private long _refreshCount;
        private long _refreshFailureCount;
        private long _refreshTotalLatencyMs;
        private long _lastRefreshLatencyMs;
        private readonly object _telemetryLock = new();
        private readonly object _providerFailureLock = new();
        private readonly Dictionary<string, ProviderFailureState> _providerFailureStates = new(StringComparer.OrdinalIgnoreCase);
        private DateTime? _lastRefreshCompletedUtc;
        private string? _lastRefreshError;
        private const int CircuitBreakerFailureThreshold = 3;
        private static readonly TimeSpan CircuitBreakerBaseBackoff = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan CircuitBreakerMaxBackoff = TimeSpan.FromMinutes(30);

        private sealed class ProviderFailureState
        {
            public int ConsecutiveFailures { get; set; }
    
        public DateTime? CircuitOpenUntilUtc { get; set; }
    
        public string? LastError { get; set; }
        }
    

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
            IEnumerable<IProviderService> providers)
        {
            this._logger = logger;
            this._loggerFactory = loggerFactory;
            this._database = database;
            this._notificationService = notificationService;
            this._httpClientFactory = httpClientFactory;
            this._configService = configService;
            this._pathProvider = pathProvider;
            this._providers = providers;
        }
    

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            this._logger.LogInformation("Starting...");

            this._notificationService.Initialize();
            this.InitializeProviders();

            var isEmpty = await this._database.IsHistoryEmptyAsync();
            if (isEmpty)
            {
                // First-time startup: scan for keys and populate the database.
                // Fire as a background task so the HTTP server starts serving immediately.
                // The Slim UI's rapid-poll will pick up the data once the refresh completes.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        this._logger.LogInformation("First-time startup: scanning for keys and seeding database.");
                        await this._configService.ScanForKeysAsync();
                        await this.TriggerRefreshAsync(forceAll: true);
                        this._logger.LogInformation("First-time data seeding complete.");
                    }
                    catch (Exception ex)
                    {
                        this._logger.LogError(ex, "Error during first-time data seeding.");
                    }
                }, stoppingToken);
            }
            else
            {
                // Database has existing data — serve it immediately WITHOUT refreshing all providers.
                // Do NOT hammer 3rd party APIs on startup. The scheduled interval will refresh on time.
                this._logger.LogInformation("Startup: serving cached data from database (next refresh in {Minutes}m).", this._refreshInterval.TotalMinutes);

                // Only do targeted refresh for system providers that need immediate correctness
                // All other providers will be refreshed on the normal scheduled interval
                _ = Task.Run(async () =>
                {
                    try
                    {
                        this._logger.LogDebug("Startup: running targeted refresh for system providers...");
                        await this.TriggerRefreshAsync(
                            forceAll: true,
                            includeProviderIds: ProviderMetadataCatalog.GetStartupRefreshProviderIds());
                        this._logger.LogDebug("Startup: targeted refresh complete.");
                    }
                    catch (Exception ex)
                    {
                        this._logger.LogWarning(ex, "Startup targeted refresh failed");
                    }
                }, stoppingToken);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    this._logger.LogDebug("Next refresh in {Minutes} minutes...", this._refreshInterval.TotalMinutes);
                    await Task.Delay(this._refreshInterval, stoppingToken);
                    await this.TriggerRefreshAsync();
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

            this._logger.LogDebug("Initialized {Count} providers: {Providers}",
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

            await this._refreshSemaphore.WaitAsync();
            try
            {
                this._logger.LogDebug("Starting data refresh - {Time}", DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));

                this._logger.LogInformation("Refreshing...");

                this._logger.LogInformation("Loading provider configurations...");
                var configs = await this._configService.GetConfigsAsync();
                this._logger.LogInformation("Found {Count} total configurations", configs.Count);

                foreach (var c in configs)
                {
                    var hasKey = !string.IsNullOrEmpty(c.ApiKey);
                    this._logger.LogInformation("Provider {ProviderId}: {Status}",
                        c.ProviderId, hasKey ? $"Has API key ({c.ApiKey?.Length ?? 0} chars)" : "NO API KEY");
                }

                this.EnsureAutoIncludedConfigs(configs);

                var activeConfigs = configs.Where(c =>
                    forceAll ||
                    this.IsAutoIncludedProviderConfig(c.ProviderId) ||
                    !string.IsNullOrEmpty(c.ApiKey)).ToList();

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

                // Debug: log which providers we're about to refresh
                foreach (var config in activeConfigs.OrderBy(c => c.ProviderId))
                {
                    this._logger.LogDebug("Active config: {ProviderId} (Key present: {HasKey})",
                        config.ProviderId, !string.IsNullOrEmpty(config.ApiKey));
                }

                foreach (var config in configs)
                {
                    await this._database.StoreProviderAsync(config);
                }

                var refreshableConfigs = bypassCircuitBreaker
                    ? activeConfigs
                    : this.GetRefreshableConfigs(activeConfigs, forceAll);
                var circuitSkippedCount = activeConfigs.Count - refreshableConfigs.Count;
                if (circuitSkippedCount > 0)
                {
                    this._logger.LogInformation("Circuit breaker skipping {Count} provider(s) this cycle", circuitSkippedCount);
                }

                if (refreshableConfigs.Count > 0)
                {
                    this._logger.LogDebug("Querying {Count} providers with API keys...", refreshableConfigs.Count);
                    this._logger.LogInformation("Querying {Count} providers", refreshableConfigs.Count);

                    var providerIdsToQuery = refreshableConfigs
                        .Select(c => c.ProviderId)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    var usages = await this._providerManager.GetAllUsageAsync(
                        forceRefresh: true,
                        progressCallback: _ => { },
                        includeProviderIds: providerIdsToQuery);

                    this._logger.LogDebug("Received {Count} total usage results", usages.Count());

                    // Validate detail contract - mark providers with invalid details as unavailable
                    var validatedUsages = this.ValidateDetailContract(usages).ToList();
                    this._logger.LogDebug("Validated {Count} usage results after detail contract check", validatedUsages.Count);

                    var activeProviderIds = refreshableConfigs.Select(c => c.ProviderId).ToHashSet(StringComparer.OrdinalIgnoreCase);

                    // Allow dynamic children (e.g. antigravity.* / codex.*) through the filter
                    // when their parent provider is active.
                    // Filter out entries where the API Key was missing to prevent logging empty data over and over.
                    var filteredUsages = validatedUsages.Where(u =>
                        IsUsageForAnyActiveProvider(activeProviderIds, u.ProviderId) &&
                        // Drop unconfigured providers that returned no usage
                        // Check regardless of description - if no usage data and not available, it's a placeholder
                        !(u.RequestsAvailable == 0 && u.RequestsUsed == 0 && u.RequestsPercentage == 0 && !u.IsAvailable)
                    ).ToList();

                    this._logger.LogDebug("Provider query results:");
                    foreach (var usage in filteredUsages)
                    {
                        var status = usage.IsAvailable ? "OK" : "FAILED";
                        var msg = usage.IsAvailable
                            ? $"{usage.RequestsPercentage:F1}% used"
                            : usage.Description;
                        this._logger.LogDebug("  {ProviderId}: [{Status}] {Message}", usage.ProviderId, status, msg);
                    }

                    this.UpdateProviderFailureStates(refreshableConfigs, filteredUsages);

                    // Auto-register any dynamic sub-providers (e.g. antigravity models) that aren't in config yet
                    // This ensures we have a provider record for foreign keys
                    foreach (var usage in filteredUsages)
                    {
                        // Auto-register OR update dynamic sub-providers (e.g. antigravity.* / codex.*)
                        // We update even if existing to ensure Friendly Name changes (like adding prefix) are persisted
                        if (IsDynamicChildOfAnyActiveProvider(activeProviderIds, usage.ProviderId) ||
                            !activeProviderIds.Contains(usage.ProviderId))
                        {
                            if (!activeProviderIds.Contains(usage.ProviderId))
                            {
                                this._logger.LogInformation("Auto-registering dynamic provider: {ProviderId}", usage.ProviderId);
                            }

                            var dynamicConfig = new ProviderConfig
                            {
                                ProviderId = usage.ProviderId,
                                Type = usage.IsQuotaBased ? "quota-based" : "pay-as-you-go",
                                AuthSource = usage.AuthSource,
                                ApiKey = "dynamic" // Placeholder to mark as active
                            };

                            await this._database.StoreProviderAsync(dynamicConfig, usage.ProviderName);

                            if (!activeProviderIds.Contains(usage.ProviderId))
                            {
                                activeProviderIds.Add(usage.ProviderId);
                            }
                        }
                    }

                    await this._database.StoreHistoryAsync(filteredUsages);
                    this._logger.LogDebug("Stored {Count} provider histories", filteredUsages.Count);

                    foreach (var usage in filteredUsages.Where(u => !string.IsNullOrEmpty(u.RawJson)))
                    {
                        await this._database.StoreRawSnapshotAsync(usage.ProviderId, usage.RawJson!, usage.HttpStatus);
                    }

                    await this.DetectResetEventsAsync(filteredUsages);
                    var prefs = await this._configService.GetPreferencesAsync();
                    CheckUsageAlerts(filteredUsages, prefs, configs);

                    this._logger.LogInformation("Done: {Count} records", filteredUsages.Count);
                    this._logger.LogDebug("Refresh complete. Stored {Count} provider histories", filteredUsages.Count);
                }
                else
                {
                    this._logger.LogDebug("No refreshable providers currently available.");
                    this._logger.LogInformation("No providers configured or all providers are in backoff.");
                }

                await this._database.CleanupOldSnapshotsAsync();
                await this._database.OptimizeAsync();
                this._logger.LogInformation("Cleanup complete");
                refreshSucceeded = true;
            }
            catch (Exception ex)
            {
                this._logger.LogError(ex, "Refresh failed: {Message}", ex.Message);
                Program.ReportError($"Refresh failed: {ex.Message}", this._pathProvider, this._logger);
                refreshError = ex.Message;
            }
            finally
            {
                refreshStopwatch.Stop();
                this.RecordRefreshTelemetry(refreshStopwatch.Elapsed, refreshSucceeded, refreshError);
                this._refreshSemaphore.Release();
            }
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
                LastError = lastRefreshError
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
    

        private List<ProviderConfig> GetRefreshableConfigs(List<ProviderConfig> activeConfigs, bool forceAll)
        {
            if (forceAll || activeConfigs.Count == 0)
            {
                return activeConfigs;
            }

            var now = DateTime.UtcNow;
            var refreshable = new List<ProviderConfig>(activeConfigs.Count);

            lock (this._providerFailureLock)
            {
                foreach (var config in activeConfigs)
                {
                    if (!this._providerFailureStates.TryGetValue(config.ProviderId, out var state))
                    {
                        refreshable.Add(config);
                        continue;
                    }

                    if (state.CircuitOpenUntilUtc.HasValue && state.CircuitOpenUntilUtc.Value > now)
                    {
                        this._logger.LogDebug(
                            "Circuit open for {ProviderId}; skipping until {RetryUtc:HH:mm:ss} UTC",
                            config.ProviderId,
                            state.CircuitOpenUntilUtc.Value);
                        continue;
                    }

                    state.CircuitOpenUntilUtc = null;
                    refreshable.Add(config);
                }
            }

            return refreshable;
        }
    

        private void UpdateProviderFailureStates(IReadOnlyCollection<ProviderConfig> queriedConfigs, IReadOnlyCollection<ProviderUsage> usages)
        {
            if (queriedConfigs.Count == 0)
            {
                return;
            }

            lock (this._providerFailureLock)
            {
                var now = DateTime.UtcNow;
                foreach (var config in queriedConfigs)
                {
                    var providerUsages = usages
                        .Where(u => IsUsageForProvider(config.ProviderId, u.ProviderId))
                        .ToList();
                    var isSuccess = providerUsages.Any(IsSuccessfulUsage);

                    if (isSuccess)
                    {
                        if (this._providerFailureStates.Remove(config.ProviderId))
                        {
                            this._logger.LogDebug("Circuit reset for {ProviderId}", config.ProviderId);
                        }
                        continue;
                    }

                    if (!this._providerFailureStates.TryGetValue(config.ProviderId, out var state))
                    {
                        state = new ProviderFailureState();
                        this._providerFailureStates[config.ProviderId] = state;
                    }

                    state.ConsecutiveFailures++;
                    state.LastError = GetFailureMessage(providerUsages);

                    if (state.ConsecutiveFailures >= CircuitBreakerFailureThreshold)
                    {
                        var backoffDelay = GetCircuitBreakerDelay(state.ConsecutiveFailures);
                        state.CircuitOpenUntilUtc = now.Add(backoffDelay);

                        this._logger.LogWarning(
                            "Circuit opened for {ProviderId} after {Failures} failures; retry at {RetryUtc:HH:mm:ss} UTC ({DelayMinutes:F1} min). Last error: {Error}",
                            config.ProviderId,
                            state.ConsecutiveFailures,
                            state.CircuitOpenUntilUtc.Value,
                            backoffDelay.TotalMinutes,
                            state.LastError);
                    }
                    else
                    {
                        this._logger.LogDebug(
                            "Provider {ProviderId} failure {Failures}/{Threshold}: {Error}",
                            config.ProviderId,
                            state.ConsecutiveFailures,
                            CircuitBreakerFailureThreshold,
                            state.LastError);
                    }
                }
            }
        }
    

        private static bool IsUsageForProvider(string providerId, string usageProviderId)
        {
            if (usageProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return usageProviderId.StartsWith($"{providerId}.", StringComparison.OrdinalIgnoreCase);
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
    

        private static bool IsDynamicChildOfAnyActiveProvider(HashSet<string> activeProviderIds, string usageProviderId)
        {
            return activeProviderIds.Any(providerId =>
                usageProviderId.StartsWith($"{providerId}.", StringComparison.OrdinalIgnoreCase));
        }
    

        private static bool IsSuccessfulUsage(ProviderUsage usage)
        {
            if (!usage.IsAvailable)
            {
                return false;
            }

            if (usage.HttpStatus >= 400)
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(usage.Description) ||
                   !usage.Description.StartsWith("[Error]", StringComparison.OrdinalIgnoreCase);
        }
    

        private static string GetFailureMessage(IReadOnlyCollection<ProviderUsage> providerUsages)
        {
            if (providerUsages.Count == 0)
            {
                return "No usage data returned";
            }

            var failedUsage = providerUsages.FirstOrDefault(u => !IsSuccessfulUsage(u));
            if (failedUsage != null && !string.IsNullOrWhiteSpace(failedUsage.Description))
            {
                return failedUsage.Description;
            }

            return "Provider returned no successful usage entries";
        }
    

        private static TimeSpan GetCircuitBreakerDelay(int consecutiveFailures)
        {
            var backoffLevel = Math.Max(0, consecutiveFailures - CircuitBreakerFailureThreshold);
            var exponent = Math.Min(backoffLevel, 6);
            var seconds = CircuitBreakerBaseBackoff.TotalSeconds * Math.Pow(2, exponent);
            return TimeSpan.FromSeconds(Math.Min(seconds, CircuitBreakerMaxBackoff.TotalSeconds));
        }
    

        private static bool IsInQuietHours(AppPreferences prefs)
        {
            if (!prefs.EnableQuietHours)
            {
                return false;
            }

            if (!TimeSpan.TryParse(prefs.QuietHoursStart, out var start) ||
                !TimeSpan.TryParse(prefs.QuietHoursEnd, out var end))
            {
                return false;
            }

            var now = DateTime.Now.TimeOfDay;
            if (start == end)
            {
                return true;
            }

            if (start < end)
            {
                return now >= start && now < end;
            }

            return now >= start || now < end;
        }
    

        public void CheckUsageAlerts(List<ProviderUsage> usages, AppPreferences prefs, List<ProviderConfig> configs)
        {
            if (!prefs.EnableNotifications || !prefs.NotifyOnUsageThreshold || IsInQuietHours(prefs))
            {
                return;
            }

            foreach (var usage in usages)
            {
                var config = configs.FirstOrDefault(c => c.ProviderId.Equals(usage.ProviderId, StringComparison.OrdinalIgnoreCase));
                var usedPercentage = UsageMath.GetEffectiveUsedPercent(usage);
                if (config != null && config.EnableNotifications && usedPercentage >= prefs.NotificationThreshold)
                {
                    this._notificationService.ShowUsageAlert(usage.ProviderName, usedPercentage);
                }
            }
        }
    

        private async Task DetectResetEventsAsync(List<ProviderUsage> currentUsages)
        {
            this._logger.LogDebug("Checking for reset events...");

            // Batch load history for all relevant providers (latest 2 records for each)
            var allHistory = await this._database.GetRecentHistoryAsync(2);
            var historyMap = allHistory.GroupBy(h => h.ProviderId).ToDictionary(g => g.Key, g => g.ToList());

            foreach (var usage in currentUsages)
            {
                try
                {
                    if (!historyMap.TryGetValue(usage.ProviderId, out var history) || history.Count < 2)
                    {
                        // For sub-providers (models) or providers with explicit reset times, 
                        // lack of history is expected and shouldn't be noisy.
                        if (usage.ProviderId.Contains('.') || usage.NextResetTime != null)
                        {
                            this._logger.LogTrace("{ProviderId}: Initial record stored, waiting for history", usage.ProviderId);
                        }
                        else
                        {
                            this._logger.LogDebug("{ProviderId}: Not enough history for reset detection", usage.ProviderId);
                        }
                        continue;
                    }

                    var current = history[0];
                    var previous = history[1];

                    bool isReset = false;
                    string resetReason = string.Empty;

                    // 1. Explicit Reset Detection (via NextResetTime moving forward)
                    if (current.NextResetTime.HasValue && previous.NextResetTime.HasValue)
                    {
                        if (current.NextResetTime.Value > previous.NextResetTime.Value.AddMinutes(1)) // Use small buffer
                        {
                            isReset = true;
                            resetReason = $"Reset detected via schedule: {previous.NextResetTime:HH:mm} -> {current.NextResetTime:HH:mm}";
                        }
                    }

                    // 2. Heuristic Reset Detection (if not already detected)
                    if (!isReset)
                    {
                        if (usage.IsQuotaBased)
                        {
                            var previousUsedPercent = UsageMath.GetEffectiveUsedPercent(previous);
                            var currentUsedPercent = UsageMath.GetEffectiveUsedPercent(current);

                            if (previousUsedPercent > 50 && currentUsedPercent < previousUsedPercent * 0.3)
                            {
                                isReset = true;
                                resetReason = $"Quota reset: {previousUsedPercent:F1}% -> {currentUsedPercent:F1}% used";
                            }
                        }
                        else
                        {
                            if (previous.RequestsUsed > current.RequestsUsed)
                            {
                                var dropPercent = (previous.RequestsUsed - current.RequestsUsed) / previous.RequestsUsed * 100;
                                if (dropPercent > 20)
                                {
                                    isReset = true;
                                    resetReason = $"Usage reset: ${previous.RequestsUsed:F2} -> ${current.RequestsUsed:F2} ({dropPercent:F0}% drop)";
                                }
                            }
                        }
                    }

                    if (isReset)
                    {
                        await this._database.StoreResetEventAsync(
                            usage.ProviderId,
                            usage.ProviderName,
                            previous.RequestsUsed,
                            current.RequestsUsed,
                            usage.IsQuotaBased ? "quota" : "usage"
                        );

                        var prefs = await this._configService.GetPreferencesAsync();
                        var configs = await this._configService.GetConfigsAsync();
                        var config = configs.FirstOrDefault(c => c.ProviderId.Equals(usage.ProviderId, StringComparison.OrdinalIgnoreCase));

                        if (prefs.EnableNotifications &&
                            prefs.NotifyOnQuotaExceeded &&
                            !IsInQuietHours(prefs) &&
                            config != null &&
                            config.EnableNotifications)
                        {
                            var details = usage.IsQuotaBased ? "Quota reset detected." : "Usage reset detected.";
                            this._notificationService.ShowQuotaExceeded(usage.ProviderName, details);
                        }

                        this._logger.LogInformation("{ProviderId} reset: {Reason}", usage.ProviderId, resetReason);
                    }
                }
                catch (Exception ex)
                {
                    this._logger.LogWarning(ex, "Reset check failed for {ProviderId}: {Message}", usage.ProviderId, ex.Message);
                }
            }
        }
    

        public async Task<(bool success, string message, int status)> CheckProviderAsync(string providerId)
        {
            if (this._providerManager == null)
            {
                return (false, "ProviderManager not initialized", 503);
            }

            try
            {
                await this._refreshSemaphore.WaitAsync();
                try
                {
                    var usages = await this._providerManager.GetUsageAsync(providerId);
                    var usage = usages.FirstOrDefault();

                    if (usage == null)
                        return (false, "No usage data returned", 404);

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
                        IsQuotaBased = usage.IsQuotaBased
                    };
                }
                else
                {
                    yield return usage;
                }
            }
        }
    }
    

    public sealed class RefreshTelemetrySnapshot
    {
        public long RefreshCount { get; init; }
    
    public long RefreshSuccessCount { get; init; }
    
    public long RefreshFailureCount { get; init; }
    
    public double ErrorRatePercent { get; init; }
    
    public double AverageLatencyMs { get; init; }
    
    public long LastLatencyMs { get; init; }
    
    public DateTime? LastRefreshCompletedUtc { get; init; }
    
    public string? LastError { get; init; }
    }

}
