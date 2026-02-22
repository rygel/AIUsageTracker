using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageTracker.Infrastructure.Configuration;

public class JsonConfigLoader : IConfigLoader
{
    private readonly ILogger<JsonConfigLoader> _logger;
    private readonly ILogger<TokenDiscoveryService> _tokenDiscoveryLogger;

    public JsonConfigLoader(ILogger<JsonConfigLoader>? logger = null, ILogger<TokenDiscoveryService>? tokenDiscoveryLogger = null)
    {
        _logger = logger ?? NullLogger<JsonConfigLoader>.Instance;
        _tokenDiscoveryLogger = tokenDiscoveryLogger ?? NullLogger<TokenDiscoveryService>.Instance;
    }

    private string GetTrackerConfigPath() => 
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ai-consumption-tracker", "auth.json");

    private string GetProvidersConfigPath() => 
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ai-consumption-tracker", "providers.json");

    public async Task<List<ProviderConfig>> LoadConfigAsync()
    {
        var authPaths = new List<string>
        {
            GetTrackerConfigPath(),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "opencode", "auth.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "opencode", "auth.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "opencode", "auth.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".opencode", "auth.json")
        };

        var providerPaths = new List<string>
        {
            GetProvidersConfigPath(),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "opencode", "providers.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "opencode", "providers.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "opencode", "providers.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".opencode", "providers.json")
        };

        // Dictionary to merge configs: ProviderId -> Config
        var mergedConfigs = new Dictionary<string, ProviderConfig>(StringComparer.OrdinalIgnoreCase);

        // helper to process a file and merge into dictionary
        async Task ProcessFile(string path, bool isAuthFile)
        {
            if (!File.Exists(path)) return;
            try
            {
                var json = await File.ReadAllTextAsync(path);
                var rawConfigs = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (rawConfigs != null)
                {
                    foreach (var kvp in rawConfigs)
                    {
                        var providerId = kvp.Key;
                        if (providerId.Equals("kimi-for-coding", StringComparison.OrdinalIgnoreCase)) providerId = "kimi";
                        if (providerId.Equals("codex", StringComparison.OrdinalIgnoreCase)) providerId = "openai";
                        if (providerId.Equals("app_settings", StringComparison.OrdinalIgnoreCase)) continue;

                        if (!mergedConfigs.TryGetValue(providerId, out var config))
                        {
                            config = new ProviderConfig { ProviderId = providerId };
                            mergedConfigs[providerId] = config;
                        }

                        var element = kvp.Value;
                        
                        // Auth file takes precedence for keys
                        if (element.TryGetProperty("key", out var keyProp) && (isAuthFile || string.IsNullOrEmpty(config.ApiKey))) 
                        {
                            var val = keyProp.GetString();
                            if (!string.IsNullOrEmpty(val)) config.ApiKey = val;
                        }

                        // Provider file takes precedence for settings, but fallback to auth file if not set
                        if (element.TryGetProperty("type", out var typeProp)) config.Type = typeProp.GetString() ?? config.Type;
                        if (element.TryGetProperty("base_url", out var urlProp)) config.BaseUrl = urlProp.GetString() ?? config.BaseUrl;
                        if (element.TryGetProperty("show_in_tray", out var showProp)) config.ShowInTray = showProp.ValueKind == JsonValueKind.True;
                        if (element.TryGetProperty("enable_notifications", out var notifyProp)) config.EnableNotifications = notifyProp.ValueKind == JsonValueKind.True;

                        if (element.TryGetProperty("enabled_sub_trays", out var subProp) && subProp.ValueKind == JsonValueKind.Array)
                        {
                             var list = new List<string>();
                             foreach (var sub in subProp.EnumerateArray()) 
                             {
                                 var val = sub.GetString();
                                 if (val != null) list.Add(val);
                             }
                             config.EnabledSubTrays = list;
                        }

                        if (element.TryGetProperty("models", out var modelsProp) && modelsProp.ValueKind == JsonValueKind.Array)
                        {
                            try
                            {
                                config.Models = JsonSerializer.Deserialize<List<AIModelConfig>>(modelsProp.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<AIModelConfig>();
                            }
                            catch { }
                        }
                        
                        // Append source
                        if (string.IsNullOrEmpty(config.AuthSource)) config.AuthSource = $"Config: {Path.GetFileName(path)}";
                        else config.AuthSource += $", {Path.GetFileName(path)}";
                    }
                }
            }
            catch { }
        }

        // Load Auth Files first
        foreach (var path in authPaths) await ProcessFile(path, true);
        
        // Load Provider Files next (overwriting settings, keeping keys if missing or whatever logic above)
        // actually logic above says: key from auth file takes precedence. Settings overwrite.
        // So we should verify priority.
        // If I load auth first, then providers:
        // Key: Auth file sets it. Provider file only sets if empty. -> Correct (Auth file is source of truth for keys)
        // Settings: Auth file sets defaults. Provider file overwrite. -> Correct (Provider file is source of truth for settings)
        
        foreach (var path in providerPaths) await ProcessFile(path, false);

        var result = mergedConfigs.Values.ToList();

        var discoveryService = new TokenDiscoveryService(_tokenDiscoveryLogger);
        var discovered = await discoveryService.DiscoverTokensAsync();
        
        foreach (var d in discovered)
        {
            var existing = result.FirstOrDefault(r => r.ProviderId.Equals(d.ProviderId, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                result.Add(d);
            }
            else
            {
                var discoveredOpenAiSession =
                    d.ProviderId.Equals("openai", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(d.ApiKey) &&
                    !d.ApiKey.StartsWith("sk-", StringComparison.OrdinalIgnoreCase) &&
                    (d.AuthSource?.Contains("OpenCode", StringComparison.OrdinalIgnoreCase) == true ||
                     d.AuthSource?.Contains("Codex", StringComparison.OrdinalIgnoreCase) == true);

                if (string.IsNullOrEmpty(existing.ApiKey) && !string.IsNullOrEmpty(d.ApiKey) || discoveredOpenAiSession)
                {
                    existing.ApiKey = d.ApiKey;
                    existing.Description = d.Description;
                    existing.AuthSource = d.AuthSource;
                    if (string.IsNullOrEmpty(existing.BaseUrl)) existing.BaseUrl = d.BaseUrl;
                }

                // Always keep discovered classification defaults in sync.
                existing.PlanType = d.PlanType;
                existing.Type = d.Type;
            }
        }

        return result;
    }

    public async Task SaveConfigAsync(List<ProviderConfig> configs)
    {
        var authPath = GetTrackerConfigPath();
        var providersPath = GetProvidersConfigPath();

        var authDir = Path.GetDirectoryName(authPath);
        if (authDir != null && !Directory.Exists(authDir)) Directory.CreateDirectory(authDir);

        var provDir = Path.GetDirectoryName(providersPath);
        if (provDir != null && !Directory.Exists(provDir)) Directory.CreateDirectory(provDir);

        // Prepare dictionaries
        var exportAuth = new Dictionary<string, object>();
        var exportProviders = new Dictionary<string, object>();

        // Load existing files to preserve extra data (like app_settings in auth.json, or other props)
        if (File.Exists(authPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(authPath);
                var existing = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                if (existing != null) exportAuth = existing;
            }
            catch { }
        }

        if (File.Exists(providersPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(providersPath);
                var existing = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                if (existing != null) exportProviders = existing;
            }
            catch { }
        }

        foreach (var config in configs)
        {
            // 1. Update Auth (Key)
            if (exportAuth.TryGetValue(config.ProviderId, out var existingAuthObj) && existingAuthObj is JsonElement existingAuthEl)
            {
                // Deserialize to dict to modify
                var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(existingAuthEl.GetRawText()) ?? new Dictionary<string, object?>();
                dict["key"] = config.ApiKey;
                exportAuth[config.ProviderId] = dict;
            }
            else if (exportAuth.TryGetValue(config.ProviderId, out var existingAuthDictObj) && existingAuthDictObj is Dictionary<string, object?> existingAuthDict)
            {
                 existingAuthDict["key"] = config.ApiKey;
            }
            else
            {
                // Create new entry
                exportAuth[config.ProviderId] = new Dictionary<string, object?> { { "key", config.ApiKey } };
            }

            // 2. Update Providers (Settings)
            Dictionary<string, object?> provDict;
            if (exportProviders.TryGetValue(config.ProviderId, out var existingProvObj) && existingProvObj is JsonElement existingProvEl)
            {
                 provDict = JsonSerializer.Deserialize<Dictionary<string, object?>>(existingProvEl.GetRawText()) ?? new Dictionary<string, object?>();
            }
            else if (exportProviders.TryGetValue(config.ProviderId, out var existingProvDictObj) && existingProvDictObj is Dictionary<string, object?> existingProvDict)
            {
                 provDict = existingProvDict;
            }
            else
            {
                provDict = new Dictionary<string, object?>();
            }

            provDict["type"] = config.Type;
            provDict["show_in_tray"] = config.ShowInTray;
            provDict["enable_notifications"] = config.EnableNotifications;
            provDict["enabled_sub_trays"] = config.EnabledSubTrays;
            if (!string.IsNullOrEmpty(config.BaseUrl)) provDict["base_url"] = config.BaseUrl;
            
            // Note: We don't currently serialize Models back to disk in this method as UI doesn't edit them yet.
            // But if we did, it would go here.
            
            exportProviders[config.ProviderId] = provDict;
        }

        var opts = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(authPath, JsonSerializer.Serialize(exportAuth, opts));
        await File.WriteAllTextAsync(providersPath, JsonSerializer.Serialize(exportProviders, opts));
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


