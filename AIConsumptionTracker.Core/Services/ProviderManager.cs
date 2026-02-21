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
            configs.Add(new ProviderConfig
            {
                ProviderId = "antigravity",
                ApiKey = "",
                Type = "quota-based",
                PlanType = PlanType.Coding
            });
        }
        if (!configs.Any(c => c.ProviderId == "gemini-cli"))
        {
            configs.Add(new ProviderConfig
            {
                ProviderId = "gemini-cli",
                ApiKey = "",
                Type = "quota-based",
                PlanType = PlanType.Coding
            });
        }
        if (!configs.Any(c => c.ProviderId.Equals("opencode", StringComparison.OrdinalIgnoreCase) || c.ProviderId.Equals("opencode-zen", StringComparison.OrdinalIgnoreCase)))
        {
            configs.Add(new ProviderConfig { ProviderId = "opencode-zen", ApiKey = "" });
        }
        if (!configs.Any(c => c.ProviderId == "claude-code"))
        {
            configs.Add(new ProviderConfig { ProviderId = "claude-code", ApiKey = "" });
        }
        if (!configs.Any(c => c.ProviderId == "codex"))
        {
            configs.Add(new ProviderConfig
            {
                ProviderId = "codex",
                ApiKey = "",
                Type = "quota-based",
                PlanType = PlanType.Coding
            });
        }

        var results = new List<ProviderUsage>();

        var tasks = configs.Select(async config =>
        {
            return await FetchSingleProviderUsageAsync(config, progressCallback);
        });

        var nestedResults = await Task.WhenAll(tasks);
        results.AddRange(nestedResults.SelectMany(x => x));
        _lastUsages = results;
        return results;
    }

    public async Task<List<ProviderUsage>> GetUsageAsync(string providerId)
    {
        var configs = await GetConfigsAsync(forceRefresh: false);
        var config = configs.FirstOrDefault(c => c.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
        
        if (config == null)
        {
            // Try to create a temporary config if it's a known system provider
             if (providerId == "antigravity" || providerId == "gemini-cli" || providerId == "opencode-zen" || providerId == "claude-code" || providerId == "codex")
             {
                 config = new ProviderConfig { ProviderId = providerId, ApiKey = "" };
             }
             else
             {
                 throw new ArgumentException($"Provider '{providerId}' not found in configuration.");
             }
        }

        return await FetchSingleProviderUsageAsync(config, null);
    }

    private async Task<List<ProviderUsage>> FetchSingleProviderUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback)
    {
        var provider = _providers.FirstOrDefault(p => 
            p.ProviderId.Equals(config.ProviderId, StringComparison.OrdinalIgnoreCase) ||
            (p.ProviderId == "minimax" && config.ProviderId.Contains("minimax", StringComparison.OrdinalIgnoreCase)) ||
            (p.ProviderId == "xiaomi" && config.ProviderId.Contains("xiaomi", StringComparison.OrdinalIgnoreCase)) ||
            (p.ProviderId == "opencode" && config.ProviderId.Contains("opencode", StringComparison.OrdinalIgnoreCase))
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
                var usages = (await provider.GetUsageAsync(config, progressCallback)).ToList();
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
                var (isQuota, planType) = GetProviderPaymentType(config.ProviderId);
                var errorUsage = new ProviderUsage
                {
                    ProviderId = config.ProviderId,
                    ProviderName = config.ProviderId,
                    Description = ex.Message,
                    RequestsPercentage = 0,
                    IsAvailable = false,
                    IsQuotaBased = isQuota,
                    PlanType = planType
                };
                progressCallback?.Invoke(errorUsage);
                return new List<ProviderUsage> { errorUsage };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to fetch usage for {config.ProviderId}");
                var (isQuota, planType) = GetProviderPaymentType(config.ProviderId);
                var errorUsage = new ProviderUsage
                {
                    ProviderId = config.ProviderId,
                    ProviderName = config.ProviderId,
                    Description = $"[Error] {ex.Message}",
                    RequestsPercentage = 0,
                    IsAvailable = true, // Still available, just failed to fetch
                    IsQuotaBased = isQuota,
                    PlanType = planType,
                    HttpStatus = 500 // Mark as error
                };
                progressCallback?.Invoke(errorUsage);
                // Throw exception here to let the caller know it failed (for Check command)
                if (progressCallback == null) throw; 
                return new List<ProviderUsage> { errorUsage };
            }
            finally
            {
                _httpSemaphore.Release();
            }
        }
        
        // Generic fallback for any provider found in config
        var (isQuotaFallback, planTypeFallback) = GetProviderPaymentType(config.ProviderId);
        var genericUsage = new ProviderUsage 
        { 
            ProviderId = config.ProviderId, 
            ProviderName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(config.ProviderId.Replace("-", " ")),
            Description = "Usage unknown (provider integration missing)",
            RequestsPercentage = 0,
            IsAvailable = false,
            UsageUnit = "Status",
            IsQuotaBased = isQuotaFallback,
            PlanType = planTypeFallback
        };
        progressCallback?.Invoke(genericUsage);
        return new List<ProviderUsage> { genericUsage };
    }


    private static (bool IsQuota, PlanType PlanType) GetProviderPaymentType(string providerId)
    {
        // Known quota-based providers that might fall through to generic fallback
        var quotaProviders = new[] { "zai-coding-plan", "antigravity", "github-copilot", "gemini-cli" };
        
        if (quotaProviders.Any(id => providerId.Equals(id, StringComparison.OrdinalIgnoreCase)))
        {
            return (true, PlanType.Coding);
        }
        
        return (false, PlanType.Usage);
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
