using System.Text.Json;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;

namespace AIConsumptionTracker.Infrastructure.Configuration;

public class JsonConfigLoader : IConfigLoader
{
    private string GetTrackerConfigPath() => 
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ai-consumption-tracker", "auth.json");

    public async Task<List<ProviderConfig>> LoadConfigAsync()
    {
        var paths = new List<string>
        {
            GetTrackerConfigPath(),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "opencode", "auth.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "opencode", "auth.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "opencode", "auth.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".opencode", "auth.json")
        };

        var result = new List<ProviderConfig>();
        var processedProviders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(path);
                    var rawConfigs = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (rawConfigs != null)
                    {
                        foreach (var kvp in rawConfigs)
                        {
                            var providerId = kvp.Key;
                            // Alias mapping
                            if (providerId.Equals("kimi-for-coding", StringComparison.OrdinalIgnoreCase)) providerId = "kimi";
                            
                            
                            // EXCLUDE special app_settings key from provider list
                            if (providerId.Equals("app_settings", StringComparison.OrdinalIgnoreCase)) continue;

                            if (processedProviders.Contains(providerId)) continue;

                            var element = kvp.Value;
                            
                            string key = string.Empty;
                            if (element.TryGetProperty("key", out var keyProp))
                            {
                                key = keyProp.GetString() ?? string.Empty;
                            }
                            else if (element.TryGetProperty("access", out var accessProp))
                            {
                                key = accessProp.GetString() ?? string.Empty;
                            }
                             
                            string type = "api";
                            if (element.TryGetProperty("type", out var typeProp))
                            {
                                type = typeProp.GetString() ?? "api";
                            }
                            else if (element.TryGetProperty("accountType", out var accountTypeProp))
                            {
                                type = accountTypeProp.GetString() ?? "api";
                            }

                            string? baseUrl = null;
                            if (element.TryGetProperty("base_url", out var urlProp)) baseUrl = urlProp.GetString();

                            bool showInTray = false;
                            if (element.TryGetProperty("show_in_tray", out var showProp)) showInTray = showProp.ValueKind == JsonValueKind.True;

                            var enabledSubTrays = new List<string>();
                            if (element.TryGetProperty("enabled_sub_trays", out var subProp) && subProp.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var sub in subProp.EnumerateArray()) 
                                {
                                    var val = sub.GetString();
                                    if (val != null) enabledSubTrays.Add(val);
                                }
                            }

                            result.Add(new ProviderConfig
                            {
                                ProviderId = providerId,
                                ApiKey = key,
                                Type = type,
                                Limit = 100,
                                BaseUrl = baseUrl,
                                ShowInTray = showInTray,
                                EnabledSubTrays = enabledSubTrays,
                                AuthSource = $"Config: {Path.GetFileName(path)}"
                            });
                            processedProviders.Add(providerId);
                        }
                    }
                }
                catch { }
            }
        }

        var discoveryService = new TokenDiscoveryService();
        var discovered = discoveryService.DiscoverTokens();
        
        foreach (var d in discovered)
        {
            var existing = result.FirstOrDefault(r => r.ProviderId.Equals(d.ProviderId, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                result.Add(d);
            }
            else if (string.IsNullOrEmpty(existing.ApiKey) && !string.IsNullOrEmpty(d.ApiKey))
            {
                existing.ApiKey = d.ApiKey;
                existing.Description = d.Description;
                if (string.IsNullOrEmpty(existing.BaseUrl)) existing.BaseUrl = d.BaseUrl;
            }
        }

        return result;
    }

    public async Task SaveConfigAsync(List<ProviderConfig> configs)
    {
        var path = GetTrackerConfigPath();
        var directory = Path.GetDirectoryName(path);
        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var export = new Dictionary<string, object>();
        foreach (var config in configs)
        {
            // Only save if there's actually a key/config to save
            if (string.IsNullOrEmpty(config.ApiKey) && string.IsNullOrEmpty(config.BaseUrl)) continue;

            var entry = new Dictionary<string, object?>
            {
                { "key", config.ApiKey },
                { "type", config.Type },
                { "show_in_tray", config.ShowInTray },
                { "enabled_sub_trays", config.EnabledSubTrays }
            };
            if (!string.IsNullOrEmpty(config.BaseUrl)) entry["base_url"] = config.BaseUrl;

            export[config.ProviderId] = entry;
        }

        // Preserve app_settings if it exists
        if (File.Exists(path))
        {
            try
            {
                 var existingJson = await File.ReadAllTextAsync(path);
                 var existingRoot = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(existingJson);
                 if (existingRoot != null && existingRoot.TryGetValue("app_settings", out var settings))
                 {
                     export["app_settings"] = settings;
                 }
            }
            catch { }
        }

        var json = JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }

    private string GetPreferencesPath() => 
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ai-consumption-tracker", "preferences.json");

    public async Task<AppPreferences> LoadPreferencesAsync()
    {
        // 1. Try loading from auth.json (app_settings) first
        var authPath = GetTrackerConfigPath();
        if (File.Exists(authPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(authPath);
                var root = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (root != null && root.TryGetValue("app_settings", out var settingsElement))
                {
                    return JsonSerializer.Deserialize<AppPreferences>(settingsElement.GetRawText()) ?? new AppPreferences();
                }
            }
            catch { }
        }

        // 2. Fallback to old preferences.json
        var path = GetPreferencesPath();
        if (File.Exists(path))
        {
            try
            {
                var json = await File.ReadAllTextAsync(path);
                return JsonSerializer.Deserialize<AppPreferences>(json) ?? new AppPreferences();
            }
            catch { }
        }
        return new AppPreferences();
    }

    public async Task SavePreferencesAsync(AppPreferences preferences)
    {
        // Save to auth.json under "app_settings"
        var path = GetTrackerConfigPath();
        var directory = Path.GetDirectoryName(path);
        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Dictionary<string, object> root;
        if (File.Exists(path))
        {
             try 
             {
                var json = await File.ReadAllTextAsync(path);
                root = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new Dictionary<string, object>();
             }
             catch
             {
                root = new Dictionary<string, object>();
             }
        }
        else
        {
            root = new Dictionary<string, object>();
        }

        root["app_settings"] = preferences;

        var output = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, output);
    }
}

