using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Paths;
using AIUsageTracker.Infrastructure.Helpers;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageTracker.Infrastructure.Configuration;

public class JsonConfigLoader : IConfigLoader
{
    private readonly ILogger<JsonConfigLoader> _logger;
    private readonly ILogger<TokenDiscoveryService> _tokenDiscoveryLogger;
    private readonly IAppPathProvider _pathProvider;

    public JsonConfigLoader(
        ILogger<JsonConfigLoader>? logger = null,
        ILogger<TokenDiscoveryService>? tokenDiscoveryLogger = null,
        IAppPathProvider? pathProvider = null)
    {
        this._logger = logger ?? NullLogger<JsonConfigLoader>.Instance;
        this._tokenDiscoveryLogger = tokenDiscoveryLogger ?? NullLogger<TokenDiscoveryService>.Instance;
        this._pathProvider = pathProvider ?? new DefaultAppPathProvider();
    }

    private string GetTrackerConfigPath() => this._pathProvider.GetAuthFilePath();

    private string GetProvidersConfigPath() => this._pathProvider.GetProviderConfigFilePath();

    private string GetPreferencesPath() => this._pathProvider.GetPreferencesFilePath();

    public async Task<IReadOnlyList<ProviderConfig>> LoadConfigAsync()
    {
        var mergedConfigs = await this.LoadMergedConfigsAsync().ConfigureAwait(false);
        var result = mergedConfigs.Values.ToList();

        await this.ApplyDiscoveredTokensAsync(result).ConfigureAwait(false);

        ProviderMetadataCatalog.NormalizeCanonicalConfigurations(result);

        return result;
    }

    private async Task<Dictionary<string, ProviderConfig>> LoadMergedConfigsAsync()
    {
        var mergedConfigs = new Dictionary<string, ProviderConfig>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in this.GetAuthConfigPaths())
        {
            await this.MergeConfigFileAsync(mergedConfigs, path, isAuthFile: true).ConfigureAwait(false);
        }

        foreach (var path in this.GetProviderConfigPaths())
        {
            await this.MergeConfigFileAsync(mergedConfigs, path, isAuthFile: false).ConfigureAwait(false);
        }

        return mergedConfigs;
    }

    private async Task MergeConfigFileAsync(Dictionary<string, ProviderConfig> mergedConfigs, string path, bool isAuthFile)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            var rawConfigs = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (rawConfigs == null)
            {
                return;
            }

            foreach (var entry in rawConfigs)
            {
                this.MergeConfigEntry(mergedConfigs, entry, path, isAuthFile);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Failed to process config file {Path}: {Message}", path, ex.Message);
        }
    }

    private void MergeConfigEntry(
        Dictionary<string, ProviderConfig> mergedConfigs,
        KeyValuePair<string, JsonElement> entry,
        string path,
        bool isAuthFile)
    {
        var providerId = ProviderMetadataCatalog.GetCanonicalProviderId(entry.Key);
        if (providerId.Equals("app_settings", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!mergedConfigs.TryGetValue(providerId, out var config))
        {
            config = new ProviderConfig { ProviderId = providerId };
            mergedConfigs[providerId] = config;
        }

        this.ApplyElementToConfig(config, entry.Value, providerId, path, isAuthFile);
        this.AppendConfigSource(config, path);
    }

    private void ApplyElementToConfig(
        ProviderConfig config,
        JsonElement element,
        string providerId,
        string path,
        bool isAuthFile)
    {
        if (element.TryGetProperty("key", out var keyProp) && (isAuthFile || string.IsNullOrEmpty(config.ApiKey)))
        {
            var value = keyProp.GetString();
            if (!string.IsNullOrEmpty(value))
            {
                config.ApiKey = value;
            }
        }

        if (element.TryGetProperty("type", out var typeProp))
        {
            config.Type = typeProp.GetString() ?? config.Type;
        }

        if (element.TryGetProperty("base_url", out var urlProp))
        {
            config.BaseUrl = urlProp.GetString() ?? config.BaseUrl;
        }

        if (element.TryGetProperty("show_in_tray", out var showProp))
        {
            config.ShowInTray = showProp.ValueKind == JsonValueKind.True;
        }

        if (element.TryGetProperty("enable_notifications", out var notifyProp))
        {
            config.EnableNotifications = notifyProp.ValueKind == JsonValueKind.True;
        }

        if (element.TryGetProperty("enabled_sub_trays", out var subTraysProp) && subTraysProp.ValueKind == JsonValueKind.Array)
        {
            config.EnabledSubTrays = this.ReadStringList(subTraysProp);
        }

        if (element.TryGetProperty("models", out var modelsProp) && modelsProp.ValueKind == JsonValueKind.Array)
        {
            config.Models = this.TryReadModelConfigs(modelsProp, providerId, path);
        }
    }

    private List<AIModelConfig> TryReadModelConfigs(JsonElement modelsProp, string providerId, string path)
    {
        try
        {
            return JsonSerializer.Deserialize<List<AIModelConfig>>(
                       modelsProp.GetRawText(),
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new List<AIModelConfig>();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse model configuration for provider {ProviderId} from {Path}", providerId, path);
            return new List<AIModelConfig>();
        }
    }

    private List<string> ReadStringList(JsonElement arrayElement)
    {
        var values = new List<string>();
        foreach (var item in arrayElement.EnumerateArray())
        {
            var value = item.GetString();
            if (value != null)
            {
                values.Add(value);
            }
        }

        return values;
    }

    private void AppendConfigSource(ProviderConfig config, string path)
    {
        if (string.IsNullOrEmpty(config.AuthSource))
        {
            config.AuthSource = $"Config: {Path.GetFileName(path)}";
            return;
        }

        config.AuthSource += $", {Path.GetFileName(path)}";
    }

    private async Task ApplyDiscoveredTokensAsync(List<ProviderConfig> configs)
    {
        var discoveryService = new TokenDiscoveryService(_tokenDiscoveryLogger, _pathProvider);
        var discovered = await discoveryService.DiscoverTokensAsync().ConfigureAwait(false);

        foreach (var discoveredConfig in discovered)
        {
            var existing = configs.FirstOrDefault(config =>
                config.ProviderId.Equals(discoveredConfig.ProviderId, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                configs.Add(discoveredConfig);
                continue;
            }

            if (string.IsNullOrEmpty(existing.ApiKey) && !string.IsNullOrEmpty(discoveredConfig.ApiKey))
            {
                existing.ApiKey = discoveredConfig.ApiKey;
                existing.Description = discoveredConfig.Description;
                existing.AuthSource = discoveredConfig.AuthSource;
                if (string.IsNullOrEmpty(existing.BaseUrl))
                {
                    existing.BaseUrl = discoveredConfig.BaseUrl;
                }
            }

            existing.PlanType = discoveredConfig.PlanType;
            existing.Type = discoveredConfig.Type;
        }
    }

    private IReadOnlyList<string> GetAuthConfigPaths()
    {
        return new[] { this.GetTrackerConfigPath() }
            .Concat(this.GetLegacyTrackerAuthPaths())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IReadOnlyList<string> GetProviderConfigPaths()
    {
        return new[] { this.GetProvidersConfigPath() }
            .Concat(this.GetLegacyTrackerProvidersPaths())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task SaveConfigAsync(IEnumerable<ProviderConfig> configs)
    {
        var authPath = this.GetTrackerConfigPath();
        var providersPath = this.GetProvidersConfigPath();

        this.EnsureParentDirectoryExists(authPath);
        this.EnsureParentDirectoryExists(providersPath);

        var exportAuth = await this.LoadExportPayloadAsync(
            authPath,
            "auth config").ConfigureAwait(false);
        var exportProviders = await this.LoadExportPayloadAsync(
            providersPath,
            "provider config").ConfigureAwait(false);

        this.RemoveNonPersistedProviders(exportAuth);
        this.RemoveNonPersistedProviders(exportProviders);

        foreach (var config in configs)
        {
            this.MergeProviderConfig(exportAuth, exportProviders, config);
        }

        await this.WriteExportPayloadAsync(authPath, exportAuth).ConfigureAwait(false);
        await this.WriteExportPayloadAsync(providersPath, exportProviders).ConfigureAwait(false);
    }

    private void EnsureParentDirectoryExists(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private async Task<Dictionary<string, object>> LoadExportPayloadAsync(string path, string payloadDescription)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, object>(StringComparer.Ordinal);
        }

        try
        {
            var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json)
                   ?? new Dictionary<string, object>(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to load existing {PayloadDescription} from {Path}; continuing with a clean export payload",
                payloadDescription,
                path);
            return new Dictionary<string, object>(StringComparer.Ordinal);
        }
    }

    private void RemoveNonPersistedProviders(Dictionary<string, object> payload)
    {
        foreach (var providerId in payload.Keys.ToList())
        {
            if (!ProviderMetadataCatalog.ShouldPersistProviderId(providerId))
            {
                payload.Remove(providerId);
            }
        }
    }

    private void MergeProviderConfig(
        Dictionary<string, object> exportAuth,
        Dictionary<string, object> exportProviders,
        ProviderConfig config)
    {
        if (!ProviderMetadataCatalog.ShouldPersistProviderId(config.ProviderId))
        {
            return;
        }

        var authDict = this.GetMutablePayloadEntry(exportAuth, config.ProviderId);
        authDict["key"] = config.ApiKey;
        exportAuth[config.ProviderId] = authDict;

        var providerDict = this.GetMutablePayloadEntry(exportProviders, config.ProviderId);
        providerDict["type"] = config.Type;
        providerDict["show_in_tray"] = config.ShowInTray;
        providerDict["enable_notifications"] = config.EnableNotifications;
        providerDict["enabled_sub_trays"] = config.EnabledSubTrays;

        if (!string.IsNullOrEmpty(config.BaseUrl))
        {
            providerDict["base_url"] = config.BaseUrl;
        }

        exportProviders[config.ProviderId] = providerDict;
    }

    private Dictionary<string, object?> GetMutablePayloadEntry(Dictionary<string, object> payload, string providerId)
    {
        if (!payload.TryGetValue(providerId, out var existingValue))
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        if (existingValue is JsonElement existingElement)
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(existingElement.GetRawText())
                   ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        if (existingValue is Dictionary<string, object?> existingDictionary)
        {
            return existingDictionary;
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal);
    }

    private async Task WriteExportPayloadAsync(string path, Dictionary<string, object> payload)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(payload, options)).ConfigureAwait(false);
    }

    public async Task<AppPreferences> LoadPreferencesAsync()
    {
        var path = this.GetPreferencesPath();
        if (File.Exists(path))
        {
            try
            {
                var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                return JsonSerializer.Deserialize<AppPreferences>(json) ?? new AppPreferences();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to load preferences from {Path}; falling back to legacy auth settings", path);
            }
        }

        return new AppPreferences();
    }

    public async Task SavePreferencesAsync(AppPreferences preferences)
    {
        var path = GetTrackerConfigPath();
        var preferencesPath = this.GetPreferencesPath();
        var directory = Path.GetDirectoryName(preferencesPath);
        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var output = JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(preferencesPath, output).ConfigureAwait(false);

        if (File.Exists(path))
        {
            _logger.LogDebug("Preferences were written to canonical path {Path}; auth.json remains provider config only.", preferencesPath);
        }
    }

    private IEnumerable<string> GetLegacyTrackerAuthPaths()
    {
        return DeprecatedPathCatalog.GetAuthFilePaths(this._pathProvider.GetUserProfileRoot());
    }

    private IEnumerable<string> GetLegacyTrackerProvidersPaths()
    {
        return DeprecatedPathCatalog.GetProviderConfigPaths(this._pathProvider.GetUserProfileRoot());
    }
}
