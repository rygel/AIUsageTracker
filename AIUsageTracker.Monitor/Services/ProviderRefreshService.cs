using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Services;
using AIUsageTracker.Infrastructure.Configuration;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Infrastructure.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace AIUsageTracker.Monitor.Services;

public class ProviderRefreshService : BackgroundService
{
    private readonly ILogger<ProviderRefreshService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IUsageDatabase _database;
    private readonly INotificationService _notificationService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConfigService _configService;
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
        ConfigService configService)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _database = database;
        _notificationService = notificationService;
        _httpClientFactory = httpClientFactory;
        _configService = configService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting...");

        _notificationService.Initialize();
        InitializeProviders();

        var isEmpty = await _database.IsHistoryEmptyAsync();
        if (isEmpty)
        {
            // First-time startup: scan for keys and populate the database.
            // Fire as a background task so the HTTP server starts serving immediately.
            // The Slim UI's rapid-poll will pick up the data once the refresh completes.
            _ = Task.Run(async () =>
            {
                try
                {
                    _logger.LogInformation("First-time startup: scanning for keys and seeding database.");
                    await _configService.ScanForKeysAsync();
                    await TriggerRefreshAsync(forceAll: true);
                    _logger.LogInformation("First-time data seeding complete.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during first-time data seeding.");
                }
            }, stoppingToken);
        }
        else
        {
            // Database has existing data — serve it immediately.
            // Do NOT refresh on startup; the scheduled interval will refresh on time.
            _logger.LogInformation("Startup: serving cached data from database (next refresh in {Minutes}m).", _refreshInterval.TotalMinutes);

            _ = Task.Run(async () =>
            {
                try
                {
                    _logger.LogInformation("Startup: forcing immediate Antigravity refresh for live model quotas.");
                    await TriggerRefreshAsync(
                        forceAll: true,
                        includeProviderIds: new[] { "antigravity" },
                        bypassCircuitBreaker: true);
                    _logger.LogInformation("Startup: Antigravity refresh complete.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Startup Antigravity refresh failed: {Message}", ex.Message);
                }
            }, stoppingToken);
        }


        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogDebug("Next refresh in {Minutes} minutes...", _refreshInterval.TotalMinutes);
                await Task.Delay(_refreshInterval, stoppingToken);
                await TriggerRefreshAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during scheduled refresh: {Message}", ex.Message);
            }
        }

        _logger.LogInformation("Stopping...");
    }

    private void InitializeProviders()
    {
        _logger.LogDebug("Initializing providers...");

        var httpClient = _httpClientFactory.CreateClient();
        var configLoader = new JsonConfigLoader(
            _loggerFactory.CreateLogger<JsonConfigLoader>(),
            _loggerFactory.CreateLogger<TokenDiscoveryService>());

        var gitHubAuthService = new GitHubAuthService(
            httpClient,
            _loggerFactory.CreateLogger<GitHubAuthService>());

        var providers = new List<IProviderService>
        {
            new ZaiProvider(httpClient, _loggerFactory.CreateLogger<ZaiProvider>()),
            new AntigravityProvider(_loggerFactory.CreateLogger<AntigravityProvider>()),
            new OpenCodeProvider(httpClient, _loggerFactory.CreateLogger<OpenCodeProvider>()),
            new OpenAIProvider(httpClient, _loggerFactory.CreateLogger<OpenAIProvider>()),
            new AnthropicProvider(_loggerFactory.CreateLogger<AnthropicProvider>()),
            new GeminiProvider(httpClient, _loggerFactory.CreateLogger<GeminiProvider>()),
            new DeepSeekProvider(httpClient, _loggerFactory.CreateLogger<DeepSeekProvider>()),
            new OpenRouterProvider(httpClient, _loggerFactory.CreateLogger<OpenRouterProvider>()),
            new KimiProvider(httpClient, _loggerFactory.CreateLogger<KimiProvider>()),
            new MinimaxProvider(httpClient, _loggerFactory.CreateLogger<MinimaxProvider>()),
            new MistralProvider(httpClient, _loggerFactory.CreateLogger<MistralProvider>()),
            new XiaomiProvider(httpClient, _loggerFactory.CreateLogger<XiaomiProvider>()),
            new GitHubCopilotProvider(
                httpClient,
                _loggerFactory.CreateLogger<GitHubCopilotProvider>(),
                gitHubAuthService),
            new ClaudeCodeProvider(_loggerFactory.CreateLogger<ClaudeCodeProvider>(), httpClient),
            new CloudCodeProvider(_loggerFactory.CreateLogger<CloudCodeProvider>()),
            new OpenCodeZenProvider(_loggerFactory.CreateLogger<OpenCodeZenProvider>()),
            new EvolveMigrationProvider(_loggerFactory.CreateLogger<EvolveMigrationProvider>()),
            new GenericPayAsYouGoProvider(httpClient, _loggerFactory.CreateLogger<GenericPayAsYouGoProvider>()),
        };

        _providerManager = new ProviderManager(
            providers,
            configLoader,
            _loggerFactory.CreateLogger<ProviderManager>());

        _logger.LogDebug("Initialized {Count} providers: {Providers}",
            providers.Count, string.Join(", ", providers.Select(p => p.ProviderId)));

        _logger.LogInformation("Loaded {Count} providers", providers.Count);
    }

    public async Task TriggerRefreshAsync(
        bool forceAll = false,
        IReadOnlyCollection<string>? includeProviderIds = null,
        bool bypassCircuitBreaker = false)
    {
        var refreshStopwatch = Stopwatch.StartNew();
        var refreshSucceeded = false;
        string? refreshError = null;

        if (_providerManager == null)
        {
            _logger.LogWarning("ProviderManager not ready");
            RecordRefreshTelemetry(refreshStopwatch.Elapsed, false, "ProviderManager not ready");
            return;
        }

        await _refreshSemaphore.WaitAsync();
        try
        {
            _logger.LogDebug("Starting data refresh - {Time}", DateTime.Now.ToString("HH:mm:ss"));

            _logger.LogInformation("Refreshing...");

            _logger.LogDebug("Loading provider configurations...");
            var configs = await _configService.GetConfigsAsync();
            _logger.LogDebug("Found {Count} total configurations", configs.Count);

            foreach (var c in configs)
            {
                var hasKey = !string.IsNullOrEmpty(c.ApiKey);
                _logger.LogDebug("Provider {ProviderId}: {Status}",
                    c.ProviderId, hasKey ? $"Has API key ({c.ApiKey?.Length ?? 0} chars)" : "NO API KEY");
            }

            // Always include system providers that don't require API keys (mirrors ProviderManager.FetchAllUsageInternal)
            if (!configs.Any(c => c.ProviderId.Equals("antigravity", StringComparison.OrdinalIgnoreCase)))
                configs.Add(new ProviderConfig
                {
                    ProviderId = "antigravity",
                    ApiKey = "",
                    Type = "quota-based",
                    PlanType = PlanType.Coding
                });
            if (!configs.Any(c => c.ProviderId.Equals("gemini-cli", StringComparison.OrdinalIgnoreCase)))
                configs.Add(new ProviderConfig
                {
                    ProviderId = "gemini-cli",
                    ApiKey = "",
                    Type = "quota-based",
                    PlanType = PlanType.Coding
                });
            if (!configs.Any(c => c.ProviderId.Equals("cloud-code", StringComparison.OrdinalIgnoreCase)))
                configs.Add(new ProviderConfig { ProviderId = "cloud-code", ApiKey = "" });
            // "antigravity", "gemini-cli", "cloud-code" are known system providers that do not require an API key to work.
            // Other providers MUST have an API key to be considered active.
            var systemProviders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
            { 
                "antigravity", "gemini-cli", "cloud-code" 
            };

            var activeConfigs = configs.Where(c =>
                forceAll ||
                systemProviders.Contains(c.ProviderId) ||
                c.ProviderId.StartsWith("antigravity.", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(c.ApiKey)).ToList();

            if (includeProviderIds != null && includeProviderIds.Count > 0)
            {
                var includeSet = includeProviderIds
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                activeConfigs = activeConfigs
                    .Where(c => includeSet.Contains(c.ProviderId))
                    .ToList();
            }

            var skippedCount = configs.Count - activeConfigs.Count;


            _logger.LogInformation("Providers: {Available} available, {Initialized} initialized", configs.Count, activeConfigs.Count);

            if (skippedCount > 0)
            {
                _logger.LogDebug("Skipping {Count} providers without API keys", skippedCount);
            }

            foreach (var config in configs)
            {
                await _database.StoreProviderAsync(config);
            }

            var refreshableConfigs = bypassCircuitBreaker
                ? activeConfigs
                : GetRefreshableConfigs(activeConfigs, forceAll);
            var circuitSkippedCount = activeConfigs.Count - refreshableConfigs.Count;
            if (circuitSkippedCount > 0)
            {
                _logger.LogInformation("Circuit breaker skipping {Count} provider(s) this cycle", circuitSkippedCount);
            }

            if (refreshableConfigs.Count > 0)
            {
                _logger.LogDebug("Querying {Count} providers with API keys...", refreshableConfigs.Count);
                _logger.LogInformation("Querying {Count} providers", refreshableConfigs.Count);

                var providerIdsToQuery = refreshableConfigs
                    .Select(c => c.ProviderId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var usages = await _providerManager.GetAllUsageAsync(
                    forceRefresh: true,
                    progressCallback: _ => { },
                    includeProviderIds: providerIdsToQuery);

                _logger.LogDebug("Received {Count} total usage results", usages.Count());

                var activeProviderIds = refreshableConfigs.Select(c => c.ProviderId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                
                // Allow dynamic children (e.g. antigravity.claude-3-5-sonnet) through the filter even if not in config explicitly yet.
                // Filter out entries where the API Key was missing to prevent logging empty data over and over.
                var filteredUsages = usages.Where(u => 
                    (activeProviderIds.Contains(u.ProviderId) || 
                    (u.ProviderId.StartsWith("antigravity.") && activeProviderIds.Contains("antigravity"))) &&
                    // Drop unconfigured providers that returned no usage
                    !(u.RequestsAvailable == 0 && u.RequestsUsed == 0 && u.RequestsPercentage == 0 && !u.IsAvailable && (u.Description?.Contains("API Key", StringComparison.OrdinalIgnoreCase) == true || u.Description?.Contains("configured", StringComparison.OrdinalIgnoreCase) == true))
                ).ToList();

                _logger.LogDebug("Provider query results:");
                foreach (var usage in filteredUsages)
                {
                    var status = usage.IsAvailable ? "OK" : "FAILED";
                    var msg = usage.IsAvailable
                        ? $"{usage.RequestsPercentage:F1}% used"
                        : usage.Description;
                    _logger.LogDebug("  {ProviderId}: [{Status}] {Message}", usage.ProviderId, status, msg);
                }

                UpdateProviderFailureStates(refreshableConfigs, filteredUsages);

                // Auto-register any dynamic sub-providers (e.g. antigravity models) that aren't in config yet
                // This ensures we have a provider record for foreign keys
                foreach (var usage in filteredUsages)
                {
                    // Auto-register OR update dynamic sub-providers (e.g. antigravity models)
                    // We update even if existing to ensure Friendly Name changes (like adding prefix) are persisted
                    if (usage.ProviderId.StartsWith("antigravity.") || !activeProviderIds.Contains(usage.ProviderId))
                    {
                        if (!activeProviderIds.Contains(usage.ProviderId))
                        {
                            _logger.LogInformation("Auto-registering dynamic provider: {ProviderId}", usage.ProviderId);
                        }
                        
                        var dynamicConfig = new ProviderConfig
                        {
                            ProviderId = usage.ProviderId,
                            Type = usage.PlanType == Core.Models.PlanType.Coding ? "coding" : "usage",
                            AuthSource = usage.AuthSource,
                            ApiKey = "dynamic" // Placeholder to mark as active
                        };
                        
                        await _database.StoreProviderAsync(dynamicConfig, usage.ProviderName);
                        
                        if (!activeProviderIds.Contains(usage.ProviderId))
                        {
                            activeProviderIds.Add(usage.ProviderId);
                        }
                    }
                }

                await _database.StoreHistoryAsync(filteredUsages);
                _logger.LogDebug("Stored {Count} provider histories", filteredUsages.Count);

                foreach (var usage in filteredUsages.Where(u => !string.IsNullOrEmpty(u.RawJson)))
                {
                    await _database.StoreRawSnapshotAsync(usage.ProviderId, usage.RawJson!, usage.HttpStatus);
                }

                await DetectResetEventsAsync(filteredUsages);
                var prefs = await _configService.GetPreferencesAsync();
                CheckUsageAlerts(filteredUsages, prefs, configs);

                _logger.LogInformation("Done: {Count} records", filteredUsages.Count);
                _logger.LogDebug("Refresh complete. Stored {Count} provider histories", filteredUsages.Count);
            }
            else
            {
                _logger.LogDebug("No refreshable providers currently available.");
                _logger.LogInformation("No providers configured or all providers are in backoff.");
            }

            await _database.CleanupOldSnapshotsAsync();
            await _database.CleanupEmptyHistoryAsync();
            await _database.OptimizeAsync();
            _logger.LogInformation("Cleanup complete");
            refreshSucceeded = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh failed: {Message}", ex.Message);
            Program.ReportError($"Refresh failed: {ex.Message}");
            refreshError = ex.Message;
        }
        finally
        {
            refreshStopwatch.Stop();
            RecordRefreshTelemetry(refreshStopwatch.Elapsed, refreshSucceeded, refreshError);
            _refreshSemaphore.Release();
        }
    }

    public RefreshTelemetrySnapshot GetRefreshTelemetrySnapshot()
    {
        var refreshCount = Interlocked.Read(ref _refreshCount);
        var refreshFailureCount = Interlocked.Read(ref _refreshFailureCount);
        var refreshTotalLatencyMs = Interlocked.Read(ref _refreshTotalLatencyMs);
        var lastRefreshLatencyMs = Interlocked.Read(ref _lastRefreshLatencyMs);

        DateTime? lastRefreshCompletedUtc;
        string? lastRefreshError;
        lock (_telemetryLock)
        {
            lastRefreshCompletedUtc = _lastRefreshCompletedUtc;
            lastRefreshError = _lastRefreshError;
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
        Interlocked.Increment(ref _refreshCount);
        Interlocked.Add(ref _refreshTotalLatencyMs, latencyMs);
        Interlocked.Exchange(ref _lastRefreshLatencyMs, latencyMs);

        if (!success)
        {
            Interlocked.Increment(ref _refreshFailureCount);
        }

        lock (_telemetryLock)
        {
            _lastRefreshCompletedUtc = DateTime.UtcNow;
            _lastRefreshError = success ? null : errorMessage;
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

        lock (_providerFailureLock)
        {
            foreach (var config in activeConfigs)
            {
                if (!_providerFailureStates.TryGetValue(config.ProviderId, out var state))
                {
                    refreshable.Add(config);
                    continue;
                }

                if (state.CircuitOpenUntilUtc.HasValue && state.CircuitOpenUntilUtc.Value > now)
                {
                    _logger.LogDebug(
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

        lock (_providerFailureLock)
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
                    if (_providerFailureStates.Remove(config.ProviderId))
                    {
                        _logger.LogDebug("Circuit reset for {ProviderId}", config.ProviderId);
                    }
                    continue;
                }

                if (!_providerFailureStates.TryGetValue(config.ProviderId, out var state))
                {
                    state = new ProviderFailureState();
                    _providerFailureStates[config.ProviderId] = state;
                }

                state.ConsecutiveFailures++;
                state.LastError = GetFailureMessage(providerUsages);

                if (state.ConsecutiveFailures >= CircuitBreakerFailureThreshold)
                {
                    var backoffDelay = GetCircuitBreakerDelay(state.ConsecutiveFailures);
                    state.CircuitOpenUntilUtc = now.Add(backoffDelay);

                    _logger.LogWarning(
                        "Circuit opened for {ProviderId} after {Failures} failures; retry at {RetryUtc:HH:mm:ss} UTC ({DelayMinutes:F1} min). Last error: {Error}",
                        config.ProviderId,
                        state.ConsecutiveFailures,
                        state.CircuitOpenUntilUtc.Value,
                        backoffDelay.TotalMinutes,
                        state.LastError);
                }
                else
                {
                    _logger.LogDebug(
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
                _notificationService.ShowUsageAlert(usage.ProviderName, usedPercentage);
            }
        }
    }

    private async Task DetectResetEventsAsync(List<ProviderUsage> currentUsages)
    {
        _logger.LogDebug("Checking for reset events...");

        // Batch load history for all relevant providers (latest 2 records for each)
        var allHistory = await _database.GetRecentHistoryAsync(2);
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
                        _logger.LogTrace("{ProviderId}: Initial record stored, waiting for history", usage.ProviderId);
                    }
                    else
                    {
                        _logger.LogDebug("{ProviderId}: Not enough history for reset detection", usage.ProviderId);
                    }
                    continue;
                }

                var current = history[0];
                var previous = history[1];

                bool isReset = false;
                string resetReason = "";

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
                    await _database.StoreResetEventAsync(
                        usage.ProviderId,
                        usage.ProviderName,
                        previous.RequestsUsed,
                        current.RequestsUsed,
                        usage.IsQuotaBased ? "quota" : "usage"
                    );

                    var prefs = await _configService.GetPreferencesAsync();
                    var configs = await _configService.GetConfigsAsync();
                    var config = configs.FirstOrDefault(c => c.ProviderId.Equals(usage.ProviderId, StringComparison.OrdinalIgnoreCase));

                    if (prefs.EnableNotifications &&
                        prefs.NotifyOnQuotaExceeded &&
                        !IsInQuietHours(prefs) &&
                        config != null &&
                        config.EnableNotifications)
                    {
                        var details = usage.IsQuotaBased ? "Quota reset detected." : "Usage reset detected.";
                        _notificationService.ShowQuotaExceeded(usage.ProviderName, details);
                    }

                    _logger.LogInformation("{ProviderId} reset: {Reason}", usage.ProviderId, resetReason);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reset check failed for {ProviderId}: {Message}", usage.ProviderId, ex.Message);
            }
        }
    }

    public async Task<(bool success, string message, int status)> CheckProviderAsync(string providerId)
    {
        if (_providerManager == null)
        {
            return (false, "ProviderManager not initialized", 503);
        }

        try
        {
            await _refreshSemaphore.WaitAsync();
            try
            {
                var usages = await _providerManager.GetUsageAsync(providerId);
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
                _refreshSemaphore.Release();
            }
        }
        catch (Exception ex)
        {
            return (false, ex.Message, 500);
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


