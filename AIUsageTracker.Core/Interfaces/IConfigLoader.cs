using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Core.Interfaces;

public interface IConfigLoader
{
    Task<List<ProviderConfig>> LoadConfigAsync();
    Task SaveConfigAsync(List<ProviderConfig> configs);
    Task<AppPreferences> LoadPreferencesAsync();
    Task SavePreferencesAsync(AppPreferences preferences);
}


