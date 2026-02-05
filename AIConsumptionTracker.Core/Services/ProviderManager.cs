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

    public List<ProviderUsage> LastUsages => _lastUsages;

    public ProviderManager(IEnumerable<IProviderService> providers, IConfigLoader configLoader, Microsoft.Extensions.Logging.ILogger<ProviderManager> logger)
    {
        _providers = providers;
        _configLoader = configLoader;
        _logger = logger;
    }

    private Task<List<ProviderUsage>>? _refreshTask;
    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);

    public async Task<List<ProviderUsage>> GetAllUsageAsync(bool forceRefresh = true)
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
                return _lastUsages;
            }

            _refreshTask = FetchAllUsageInternal();
            var currentTask = _refreshTask;
            _refreshSemaphore.Release();
            semaphoreReleased = true;
            return await currentTask;
        }
        finally
        {
            // Release semaphore if it hasn't been released yet (in case of exception before manual release)
            if (!semaphoreReleased)
            {
                _refreshSemaphore.Release();
            }
        }
    }

    private async Task<List<ProviderUsage>> FetchAllUsageInternal()
    {
        _logger.LogDebug("Starting FetchAllUsageInternal...");
        var configs = (await _configLoader.LoadConfigAsync()).ToList();
        
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
        if (!configs.Any(c => c.ProviderId == "github-copilot"))
        {
            configs.Add(new ProviderConfig { ProviderId = "github-copilot", ApiKey = "" });
        }

        var results = new List<ProviderUsage>();

        var tasks = configs.Select(async config =>
        {
            var provider = _providers.FirstOrDefault(p => 
                p.ProviderId.Equals(config.ProviderId, StringComparison.OrdinalIgnoreCase) ||
                (p.ProviderId == "anthropic" && config.ProviderId.Contains("claude", StringComparison.OrdinalIgnoreCase))
            );
            
            if (provider == null && (config.Type == "pay-as-you-go" || config.Type == "api"))
            {
                provider = _providers.FirstOrDefault(p => p.ProviderId == "generic-pay-as-you-go");
            }

            if (provider != null)
            {
                try
                {
                    _logger.LogDebug($"Fetching usage for provider: {config.ProviderId}");
                    var usages = await provider.GetUsageAsync(config);
                    foreach(var u in usages) u.AuthSource = config.AuthSource;
                    
                    _logger.LogDebug($"Success for {config.ProviderId}: {usages.Count()} items");
                    return usages;
                }
                catch (ArgumentException ex)
                {
                     // Missing API key or configuration issue -> Hide from default view
                     _logger.LogWarning($"Skipping {config.ProviderId}: {ex.Message}");
                     return new[] { new ProviderUsage
                    {
                        ProviderId = config.ProviderId,
                        ProviderName = config.ProviderId,
                        Description = ex.Message,
                        CostUsed = 0,
                        UsagePercentage = 0,
                        IsAvailable = false 
                    }};
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to fetch usage for {config.ProviderId}");
                     return new[] { new ProviderUsage
                    {
                        ProviderId = config.ProviderId,
                        ProviderName = config.ProviderId,
                        Description = $"[Error] {ex.Message}",
                        CostUsed = 0,
                        UsagePercentage = 0,
                        IsAvailable = true // Keep visible but show error for other failures
                    }};
                }
            }
            
            // Generic fallback for any provider found in config
            return new[] { new ProviderUsage 
            { 
                ProviderId = config.ProviderId, 
                ProviderName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(config.ProviderId.Replace("-", " ")),
                Description = "Connected (Generic)",
                CostUsed = 0,
                UsagePercentage = 0,
                UsageUnit = "USD",
                IsQuotaBased = false
            }};
        });

        var nestedResults = await Task.WhenAll(tasks);
        results.AddRange(nestedResults.SelectMany(x => x));
        _lastUsages = results;
        return results;
    }

    public void Dispose()
    {
        _refreshSemaphore.Dispose();
    }
}

