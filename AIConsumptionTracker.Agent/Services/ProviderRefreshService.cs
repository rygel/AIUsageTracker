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
    private readonly UsageDatabase _database;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConfigService _configService;
    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);
    private readonly TimeSpan _refreshInterval = TimeSpan.FromMinutes(5);
    private ProviderManager? _providerManager;
    private static bool _debugMode = false;

    public static void SetDebugMode(bool enabled) => _debugMode = enabled;

    public ProviderRefreshService(
        ILogger<ProviderRefreshService> logger,
        ILoggerFactory loggerFactory,
        UsageDatabase database,
        IHttpClientFactory httpClientFactory,
        ConfigService configService)
    {
        _logger = logger;
        _loggerFactory = loggerFactory;
        _database = database;
        _httpClientFactory = httpClientFactory;
        _configService = configService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Provider Refresh Service starting...");
        DebugLog("[SERVICE] Provider Refresh Service starting...");

        InitializeProviders();

        DebugLog("[SERVICE] Waiting 30 seconds before first refresh...");
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        
        // Do initial refresh after delay
        await TriggerRefreshAsync();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                DebugLog($"[SERVICE] Next refresh in {_refreshInterval.TotalMinutes} minutes...");
                await Task.Delay(_refreshInterval, stoppingToken);
                await TriggerRefreshAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during scheduled refresh");
                DebugLog($"[ERROR] Error during scheduled refresh: {ex.Message}");
            }
        }

        _logger.LogInformation("Provider Refresh Service stopping...");
        DebugLog("[SERVICE] Provider Refresh Service stopping...");
    }

    private void InitializeProviders()
    {
        DebugLog("[INIT] Initializing providers...");
        
        var httpClient = _httpClientFactory.CreateClient();
        var configLoader = new JsonConfigLoader(
            _loggerFactory.CreateLogger<JsonConfigLoader>(),
            _loggerFactory.CreateLogger<TokenDiscoveryService>());
        
        var gitHubAuthService = new GitHubAuthService(
            httpClient, 
            _loggerFactory.CreateLogger<GitHubAuthService>());

        var providers = new List<IProviderService>
        {
            // Quota-based providers
            new ZaiProvider(httpClient, _loggerFactory.CreateLogger<ZaiProvider>()),
            new AntigravityProvider(_loggerFactory.CreateLogger<AntigravityProvider>()),
            
            // Credits-based providers
            new OpenCodeProvider(httpClient, _loggerFactory.CreateLogger<OpenCodeProvider>()),
            
            // Usage-based providers
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
            
            // Generic fallback
            new GenericPayAsYouGoProvider(httpClient, _loggerFactory.CreateLogger<GenericPayAsYouGoProvider>()),
        };

        _providerManager = new ProviderManager(
            providers, 
            configLoader, 
            _loggerFactory.CreateLogger<ProviderManager>());
        
        DebugLog($"[INIT] Initialized {providers.Count} providers:");
        foreach (var p in providers)
        {
            DebugLog($"[INIT]   - {p.ProviderId}");
        }
        
        _logger.LogInformation("Initialized {Count} providers", providers.Count);
    }

    public async Task TriggerRefreshAsync()
    {
        if (_providerManager == null)
        {
            _logger.LogWarning("ProviderManager not initialized");
            DebugLog("[ERROR] ProviderManager not initialized!");
            return;
        }

        await _refreshSemaphore.WaitAsync();
        try
        {
            DebugLog("");
            DebugLog("═══════════════════════════════════════════════════════════════");
            DebugLog($"[REFRESH] Starting data refresh - {DateTime.Now:HH:mm:ss}");
            DebugLog("═══════════════════════════════════════════════════════════════");
            
            _logger.LogInformation("Starting provider data refresh...");
            
            // Get all provider configurations
            DebugLog("[CONFIG] Loading provider configurations...");
            var configs = await _configService.GetConfigsAsync();
            DebugLog($"[CONFIG] Found {configs.Count} total configurations");
            
            foreach (var c in configs)
            {
                var hasKey = !string.IsNullOrEmpty(c.ApiKey);
                DebugLog($"[CONFIG]   {c.ProviderId}: {(hasKey ? $"Has API key ({c.ApiKey?.Length ?? 0} chars)" : "NO API KEY")}");
            }
            
            // Filter to only providers with API keys
            var activeConfigs = configs.Where(c => !string.IsNullOrEmpty(c.ApiKey)).ToList();
            var skippedCount = configs.Count - activeConfigs.Count;
            
            if (skippedCount > 0)
            {
                DebugLog($"[CONFIG] Skipping {skippedCount} providers without API keys");
                _logger.LogInformation("Skipping {Count} providers without API keys", skippedCount);
            }
            
            // Store provider configurations (including those without keys for UI display)
            foreach (var config in configs)
            {
                await _database.StoreProviderAsync(config);
            }
            
            // Only query providers with API keys
            if (activeConfigs.Count > 0)
            {
                DebugLog($"[QUERY] Querying {activeConfigs.Count} providers with API keys...");
                _logger.LogInformation("Querying {Count} providers with API keys", activeConfigs.Count);
                
                // Get usage data only from providers with keys
                var usages = await _providerManager.GetAllUsageAsync(forceRefresh: true);
                
                DebugLog($"[QUERY] Received {usages.Count()} total usage results");
                
                // Filter to only include providers that have configs with API keys
                var activeProviderIds = activeConfigs.Select(c => c.ProviderId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var filteredUsages = usages.Where(u => activeProviderIds.Contains(u.ProviderId)).ToList();
                
                // Log each provider's result
                DebugLog("[RESULTS] Provider query results:");
                foreach (var usage in filteredUsages)
                {
                    var status = usage.IsAvailable ? "OK" : "FAILED";
                    var msg = usage.IsAvailable 
                        ? $"{usage.UsagePercentage:F1}% used" 
                        : usage.Description;
                    DebugLog($"[RESULTS]   {usage.ProviderId}: [{status}] {msg}");
                }
                
                // Store in provider_history (indefinite retention)
                await _database.StoreHistoryAsync(filteredUsages);
                DebugLog($"[DB] Stored {filteredUsages.Count} provider histories");
                
                // Detect and store reset events
                await DetectResetEventsAsync(filteredUsages);
                
                _logger.LogInformation("Refresh complete. Stored {Count} provider histories", filteredUsages.Count);
                DebugLog($"[REFRESH] Complete. Stored {filteredUsages.Count} provider histories");
            }
            else
            {
                DebugLog("[CONFIG] No providers with API keys configured. Nothing to refresh.");
                _logger.LogInformation("No providers with API keys configured. Nothing to refresh.");
            }
            
            // Clean up old raw snapshots (14-day retention)
            await _database.CleanupOldSnapshotsAsync();
            
            DebugLog("═══════════════════════════════════════════════════════════════");
            DebugLog("");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during provider refresh");
            DebugLog($"[ERROR] Error during provider refresh: {ex.Message}");
            DebugLog($"[ERROR] Stack trace: {ex.StackTrace}");
        }
        finally
        {
            _refreshSemaphore.Release();
        }
    }
    
    /// <summary>
    /// Detect quota/limit resets by comparing with previous usage
    /// </summary>
    private async Task DetectResetEventsAsync(List<ProviderUsage> currentUsages)
    {
        foreach (var usage in currentUsages)
        {
            try
            {
                // Get previous usage from history
                var previousHistory = await _database.GetHistoryByProviderAsync(usage.ProviderId, 2);
                if (previousHistory.Count >= 2)
                {
                    var previous = previousHistory[1]; // Second most recent
                    var current = previousHistory[0];  // Most recent
                    
                    // Detect significant decrease in usage (indicates a reset)
                    if (previous.CostUsed > current.CostUsed && 
                        previous.CostUsed - current.CostUsed > previous.CostUsed * 0.5)
                    {
                        await _database.StoreResetEventAsync(
                            usage.ProviderId,
                            usage.ProviderName,
                            previous.CostUsed,
                            current.CostUsed,
                            "monthly"
                        );
                        
                        _logger.LogInformation("Detected reset for {ProviderId}: {PreviousUsage} -> {NewUsage}",
                            usage.ProviderId, previous.CostUsed, current.CostUsed);
                        DebugLog($"[RESET] Detected reset for {usage.ProviderId}: {previous.CostUsed:F2} -> {current.CostUsed:F2}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to detect reset for {ProviderId}", usage.ProviderId);
                DebugLog($"[WARN] Failed to detect reset for {usage.ProviderId}: {ex.Message}");
            }
        }
    }

    private static void DebugLog(string message)
    {
        if (_debugMode)
        {
            Console.WriteLine(message);
        }
    }
}
