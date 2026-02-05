using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace AIConsumptionTracker.Core.Services;

public class ProviderManager
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
    private readonly object _refreshLock = new();

    public Task<List<ProviderUsage>> GetAllUsageAsync(bool forceRefresh = true)
    {
        lock (_refreshLock)
        {
            if (_refreshTask != null && !_refreshTask.IsCompleted)
            {
                _logger.LogDebug("Joining existing refresh task...");
                return _refreshTask;
            }

            if (!forceRefresh && _lastUsages.Count > 0)
            {
                return Task.FromResult(_lastUsages);
            }

            _refreshTask = FetchAllUsageInternal();
            return _refreshTask;
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
                    var usage = await provider.GetUsageAsync(config);
                    usage.AuthSource = config.AuthSource; // Propagate source
                    _logger.LogDebug($"Success for {config.ProviderId}: {usage.Description}");
                    return usage;
                }
                catch (ArgumentException ex)
                {
                     // Missing API key or configuration issue -> Hide from default view
                     _logger.LogWarning($"Skipping {config.ProviderId}: {ex.Message}");
                     return new ProviderUsage
                    {
                        ProviderId = config.ProviderId,
                        ProviderName = config.ProviderId,
                        Description = ex.Message,
                        CostUsed = 0,
                        UsagePercentage = 0,
                        IsAvailable = false 
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to fetch usage for {config.ProviderId}");
                     return new ProviderUsage
                    {
                        ProviderId = config.ProviderId,
                        ProviderName = config.ProviderId,
                        Description = $"[Error] {ex.Message}",
                        CostUsed = 0,
                        UsagePercentage = 0,
                        IsAvailable = true // Keep visible but show error for other failures
                    };
                }
            }
            
            // Generic fallback for any provider found in config
            return new ProviderUsage 
            { 
                ProviderId = config.ProviderId, 
                ProviderName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(config.ProviderId.Replace("-", " ")),
                Description = "Connected (Generic)",
                CostUsed = 0,
                UsagePercentage = 0,
                UsageUnit = "USD",
                IsQuotaBased = false
            };
        });

        results.AddRange(await Task.WhenAll(tasks));
        _lastUsages = results;
        return results;
    }
}

