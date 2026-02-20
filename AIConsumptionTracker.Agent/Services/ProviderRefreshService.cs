using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Core.Services;
using AIConsumptionTracker.Infrastructure.Configuration;
using AIConsumptionTracker.Infrastructure.Providers;
using AIConsumptionTracker.Infrastructure.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIConsumptionTracker.Agent.Services;

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
            new CodexProvider(httpClient, _loggerFactory.CreateLogger<CodexProvider>()),
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

    public async Task TriggerRefreshAsync(bool forceAll = false)
    {
        if (_providerManager == null)
        {
            _logger.LogWarning("ProviderManager not ready");
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
            if (!configs.Any(c => c.ProviderId.Equals("codex", StringComparison.OrdinalIgnoreCase)))
                configs.Add(new ProviderConfig
                {
                    ProviderId = "codex",
                    ApiKey = "",
                    Type = "quota-based",
                    PlanType = PlanType.Coding
                });

            var activeConfigs = configs.Where(c =>
                forceAll ||
                string.IsNullOrEmpty(c.ApiKey) ||   // System providers (antigravity, gemini-cli, etc.)
                c.ProviderId.StartsWith("antigravity.", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(c.ApiKey)).ToList();
            var skippedCount = 0; // All configs are now included


            _logger.LogInformation("Providers: {Available} available, {Initialized} initialized", configs.Count, activeConfigs.Count);

            if (skippedCount > 0)
            {
                _logger.LogDebug("Skipping {Count} providers without API keys", skippedCount);
            }

            foreach (var config in configs)
            {
                await _database.StoreProviderAsync(config);
            }

            if (activeConfigs.Count > 0)
            {
                _logger.LogDebug("Querying {Count} providers with API keys...", activeConfigs.Count);
                _logger.LogInformation("Querying {Count} providers", activeConfigs.Count);

                var usages = await _providerManager.GetAllUsageAsync(forceRefresh: true);

                _logger.LogDebug("Received {Count} total usage results", usages.Count());

                var activeProviderIds = activeConfigs.Select(c => c.ProviderId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                
                // Allow dynamic children (e.g. antigravity.claude-3-5-sonnet) through the filter even if not in config explicitly yet
                var filteredUsages = usages.Where(u => 
                    activeProviderIds.Contains(u.ProviderId) || 
                    (u.ProviderId.StartsWith("antigravity.") && activeProviderIds.Contains("antigravity"))
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
                _logger.LogDebug("No providers with API keys configured. Nothing to refresh.");
                _logger.LogInformation("No providers configured");
            }

            await _database.CleanupOldSnapshotsAsync();
            _logger.LogInformation("Cleanup complete");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh failed: {Message}", ex.Message);
            Program.ReportError($"Refresh failed: {ex.Message}");
        }
        finally
        {
            _refreshSemaphore.Release();
        }
    }

    public void CheckUsageAlerts(List<ProviderUsage> usages, AppPreferences prefs, List<ProviderConfig> configs)
    {
        if (!prefs.EnableNotifications) return;

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

                    if (prefs.EnableNotifications && config != null && config.EnableNotifications)
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
