using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIConsumptionTracker.Core.Services;

public class ProviderManager : IDisposable
{
    private readonly IEnumerable<IProviderService> _providers;
    private readonly IConfigLoader _configLoader;
    private readonly Microsoft.Extensions.Logging.ILogger<ProviderManager> _logger;
    private List<ProviderUsage> _lastUsages = new();
    private List<ProviderConfig>? _lastConfigs;
    private DateTime _lastConfigLoadTime = DateTime.MinValue;
    private readonly TimeSpan _configCacheValidity = TimeSpan.FromSeconds(5);

    public List<ProviderUsage> LastUsages => _lastUsages;
    public List<ProviderConfig>? LastConfigs => _lastConfigs;

    public ProviderManager(IEnumerable<IProviderService> providers, IConfigLoader configLoader, Microsoft.Extensions.Logging.ILogger<ProviderManager> logger)
    {
        _providers = providers;
        _configLoader = configLoader;
        _logger = logger;
    }

    private Task<List<ProviderUsage>>? _refreshTask;
    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);
    private readonly SemaphoreSlim _configSemaphore = new(1, 1);
    private readonly SemaphoreSlim _httpSemaphore = new(6); // Limit parallel HTTP requests to avoid congestion

    public async Task<List<ProviderConfig>> GetConfigsAsync(bool forceRefresh = false)
    {
        if (!forceRefresh && _lastConfigs != null && DateTime.UtcNow - _lastConfigLoadTime < _configCacheValidity)
        {
            _logger.LogDebug("Using cached configs");
            return _lastConfigs;
        }

        await _configSemaphore.WaitAsync();
        try
        {
            if (!forceRefresh && _lastConfigs != null && DateTime.UtcNow - _lastConfigLoadTime < _configCacheValidity)
            {
                return _lastConfigs;
            }

            _logger.LogDebug("Loading configs from file");
            var configs = (await _configLoader.LoadConfigAsync()).ToList();
            _lastConfigs = configs;
            _lastConfigLoadTime = DateTime.UtcNow;
            return configs;
        }
        finally
        {
            _configSemaphore.Release();
        }
    }

    public async Task<List<ProviderUsage>> GetAllUsageAsync(bool forceRefresh = true, Action<ProviderUsage>? progressCallback = null)
    {
        await _refreshSemaphore.WaitAsync();
        var semaphoreReleased = false;
        try
        {
            if (_refreshTask != null && !_refreshTask.IsCompleted)
            {
                _logger.LogDebug("Joining existing refresh task...");
                var existingTask = _refreshTask;
                _refreshSemaphore.Release();
                semaphoreReleased = true;
                return await existingTask;
            }

            if (!forceRefresh && _lastUsages.Count > 0)
            {
                _refreshSemaphore.Release();
                semaphoreReleased = true;
                return _lastUsages;
            }

            _refreshTask = FetchAllUsageInternal(progressCallback);
            var currentTask = _refreshTask;
            _refreshSemaphore.Release();
            semaphoreReleased = true;
            return await currentTask;
        }
        finally
        {
            // Release semaphore if it hasn't been released yet (handles exception cases)
            if (!semaphoreReleased)
            {
                _refreshSemaphore.Release();
            }
        }
    }

    private async Task<List<ProviderUsage>> FetchAllUsageInternal(Action<ProviderUsage>? progressCallback = null)
    {
        _logger.LogDebug("Starting FetchAllUsageInternal...");
        var configs = (await GetConfigsAsync(forceRefresh: true)).ToList();
        
        // Auto-add system providers that don't need auth.json
        // Check if they are already in config to avoid duplicates (though unlikely for these IDs)
        if (!configs.Any(c => c.ProviderId == "antigravity"))
        {
            configs.Add(new ProviderConfig { ProviderId = "antigravity", ApiKey = "" });
        }
        if (!configs.Any(c => c.ProviderId == "gemini-cli"))
        {
            configs.Add(new ProviderConfig { ProviderId = "gemini-cli", ApiKey = "" });
        }
        if (!configs.Any(c => c.ProviderId == "opencode-zen"))
        {
            configs.Add(new ProviderConfig { ProviderId = "opencode-zen", ApiKey = "" });
        }

        var results = new List<ProviderUsage>();

        var tasks = configs.Select(async config =>
        {
            var provider = _providers.FirstOrDefault(p => 
                p.ProviderId.Equals(config.ProviderId, StringComparison.OrdinalIgnoreCase) ||
                (p.ProviderId == "anthropic" && config.ProviderId.Contains("claude", StringComparison.OrdinalIgnoreCase)) ||
                (p.ProviderId == "minimax" && config.ProviderId.Contains("minimax", StringComparison.OrdinalIgnoreCase)) ||
                (p.ProviderId == "xiaomi" && config.ProviderId.Contains("xiaomi", StringComparison.OrdinalIgnoreCase))
            );
            
            if (provider == null && (config.Type == "pay-as-you-go" || config.Type == "api"))
            {
                provider = _providers.FirstOrDefault(p => p.ProviderId == "generic-pay-as-you-go");
            }

            if (provider != null)
            {
                await _httpSemaphore.WaitAsync();
                try
                {
                    _logger.LogDebug($"Fetching usage for provider: {config.ProviderId}");
                    var usages = await provider.GetUsageAsync(config, progressCallback);
                    foreach(var u in usages) 
                    {
                        u.AuthSource = config.AuthSource;
                        progressCallback?.Invoke(u);
                    }

                    _logger.LogDebug($"Success for {config.ProviderId}: {usages.Count()} items");
                    return usages;
                }
                catch (ArgumentException ex)
                {
                    _logger.LogWarning($"Skipping {config.ProviderId}: {ex.Message}");
                    var errorUsage = new ProviderUsage
                    {
                        ProviderId = config.ProviderId,
                        ProviderName = config.ProviderId,
                        Description = ex.Message,
                        CostUsed = 0,
                        UsagePercentage = 0,
                        IsAvailable = false
                    };
                    progressCallback?.Invoke(errorUsage);
                    return new[] { errorUsage };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to fetch usage for {config.ProviderId}");
                    var errorUsage = new ProviderUsage
                    {
                        ProviderId = config.ProviderId,
                        ProviderName = config.ProviderId,
                        Description = $"[Error] {ex.Message}",
                        CostUsed = 0,
                        UsagePercentage = 0,
                        IsAvailable = true
                    };
                    progressCallback?.Invoke(errorUsage);
                    return new[] { errorUsage };
                }
                finally
                {
                    _httpSemaphore.Release();
                }
            }
            
            // Generic fallback for any provider found in config
            var genericUsage = new ProviderUsage 
            { 
                ProviderId = config.ProviderId, 
                ProviderName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(config.ProviderId.Replace("-", " ")),
                Description = "Connected (Generic)",
                CostUsed = 0,
                UsagePercentage = 0,
                UsageUnit = "USD",
                IsQuotaBased = false
            };
            progressCallback?.Invoke(genericUsage);
            return new[] { genericUsage };
        });

        var nestedResults = await Task.WhenAll(tasks);
        results.AddRange(nestedResults.SelectMany(x => x));
        _lastUsages = results;
        return results;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshSemaphore.Dispose();
            _configSemaphore.Dispose();
            _httpSemaphore.Dispose();
        }
    }
}

