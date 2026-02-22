using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageTracker.Monitor.Services;

public class ConfigService
{
    private readonly ILogger<ConfigService> _logger;
    private readonly JsonConfigLoader _configLoader;
    private readonly TokenDiscoveryService _tokenDiscovery;

    public ConfigService(ILogger<ConfigService> logger)
    {
        _logger = logger;
        _configLoader = new JsonConfigLoader(
            logger: null,
            tokenDiscoveryLogger: null);
        _tokenDiscovery = new TokenDiscoveryService(null);
    }

    public async Task<List<ProviderConfig>> GetConfigsAsync()
    {
        try
        {
            var configs = await _configLoader.LoadConfigAsync();
            return configs;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load configs: {Message}", ex.Message);
            return new List<ProviderConfig>();
        }
    }

    public async Task SaveConfigAsync(ProviderConfig config)
    {
        try
        {
            var configs = await _configLoader.LoadConfigAsync();
            
            // Update or add
            var existing = configs.FirstOrDefault(c => 
                c.ProviderId.Equals(config.ProviderId, StringComparison.OrdinalIgnoreCase));
            
            if (existing != null)
            {
                var index = configs.IndexOf(existing);
                configs[index] = config;
            }
            else
            {
                configs.Add(config);
            }
            
            await _configLoader.SaveConfigAsync(configs);
            _logger.LogInformation("Saved: {ProviderId}", config.ProviderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save config for {ProviderId}: {Message}", config.ProviderId, ex.Message);
            throw;
        }
    }

    public async Task RemoveConfigAsync(string providerId)
    {
        try
        {
            var configs = await _configLoader.LoadConfigAsync();
            configs.RemoveAll(c => c.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
            await _configLoader.SaveConfigAsync(configs);
            _logger.LogInformation("Removed: {ProviderId}", providerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove config for {ProviderId}: {Message}", providerId, ex.Message);
            throw;
        }
    }

    public async Task<AppPreferences> GetPreferencesAsync()
    {
        try
        {
            return await _configLoader.LoadPreferencesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load preferences: {Message}", ex.Message);
            return new AppPreferences();
        }
    }

    public async Task SavePreferencesAsync(AppPreferences preferences)
    {
        try
        {
            await _configLoader.SavePreferencesAsync(preferences);
            _logger.LogInformation("Prefs saved");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save preferences: {Message}", ex.Message);
            throw;
        }
    }

    public async Task<List<ProviderConfig>> ScanForKeysAsync()
    {
        try
        {
            var discovered = await _tokenDiscovery.DiscoverTokensAsync();
            var existing = await _configLoader.LoadConfigAsync();
            
            // Merge discovered with existing
            foreach (var newConfig in discovered)
            {
                var existingConfig = existing.FirstOrDefault(c => 
                    c.ProviderId.Equals(newConfig.ProviderId, StringComparison.OrdinalIgnoreCase));
                
                if (existingConfig == null)
                {
                    existing.Add(newConfig);
                    _logger.LogInformation("Found: {ProviderId}", newConfig.ProviderId);
                }
                else if (string.IsNullOrEmpty(existingConfig.ApiKey) && !string.IsNullOrEmpty(newConfig.ApiKey))
                {
                    // Update with discovered key
                    existingConfig.ApiKey = newConfig.ApiKey;
                    existingConfig.AuthSource = newConfig.AuthSource;
                    _logger.LogInformation("Key updated: {ProviderId}", newConfig.ProviderId);
                }
            }
            
            await _configLoader.SaveConfigAsync(existing);
            return discovered;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to scan for keys: {Message}", ex.Message);
            return new List<ProviderConfig>();
        }
    }
}


