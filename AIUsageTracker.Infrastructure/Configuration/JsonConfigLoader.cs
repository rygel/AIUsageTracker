// <copyright file="JsonConfigLoader.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Helpers;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageTracker.Infrastructure.Configuration;

public class JsonConfigLoader : IConfigLoader
{
    private const string AuthConfigFileName = "auth.json";
    private const string OpenCodeDirectoryName = "opencode";
    private static readonly JsonSerializerOptions CaseInsensitiveOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly ILogger<JsonConfigLoader> _logger;
    private readonly ILogger<TokenDiscoveryService> _log;
    private readonly IAppPathProvider _pathProvider;

    public JsonConfigLoader(
        ILogger<JsonConfigLoader>? logger = null,
        ILogger<TokenDiscoveryService>? tokenDiscoveryLogger = null,
        IAppPathProvider? pathProvider = null)
    {
        this._logger = logger ?? NullLogger<JsonConfigLoader>.Instance;
        this._log = tokenDiscoveryLogger ?? NullLogger<TokenDiscoveryService>.Instance;
        this._pathProvider = pathProvider ?? new DefaultAppPathProvider();
    }

    public async Task<IReadOnlyList<ProviderConfig>> LoadConfigAsync()
    {
        var mergedConfigs = await this.LoadMergedConfigsAsync().ConfigureAwait(false);
        var result = mergedConfigs.Values.ToList();

        await this.ApplyDiscoveredTokensAsync(result).ConfigureAwait(false);

        return result;
    }

    public async Task SaveConfigAsync(IEnumerable<ProviderConfig> configs)
    {
        ArgumentNullException.ThrowIfNull(configs);

        var authPath = this.GetTrackerConfigPath();
        var providersPath = this.GetProvidersConfigPath();

        EnsureParentDirectoryExists(authPath);
        EnsureParentDirectoryExists(providersPath);

        var exportAuth = await this.LoadExportPayloadAsync(
            authPath).ConfigureAwait(false);
        var exportProviders = await this.LoadExportPayloadAsync(
            providersPath).ConfigureAwait(false);

        JsonProviderConfigExportBuilder.RemoveNonPersistedProviders(exportAuth);
        JsonProviderConfigExportBuilder.RemoveNonPersistedProviders(exportProviders);

        foreach (var config in configs)
        {
            JsonProviderConfigExportBuilder.MergeProviderConfig(exportAuth, exportProviders, config);
        }

        await WriteExportPayloadAsync(authPath, exportAuth).ConfigureAwait(false);
        await WriteExportPayloadAsync(providersPath, exportProviders).ConfigureAwait(false);
    }

    public async Task<AppPreferences> LoadPreferencesAsync()
    {
        var path = this.GetPreferencesPath();
        if (!File.Exists(path))
        {
            return new AppPreferences();
        }

        var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
        return AppPreferences.Deserialize(json);
    }

    public async Task SavePreferencesAsync(AppPreferences preferences)
    {
        var path = this.GetTrackerConfigPath();
        var preferencesPath = this.GetPreferencesPath();
        var directory = Path.GetDirectoryName(preferencesPath);
        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await JsonConfigFileStore.WriteIndentedAsync(preferencesPath, preferences).ConfigureAwait(false);

        if (File.Exists(path))
        {
            this._logger.LogDebug("Preferences were written to settings path {Path}; auth.json remains provider config only.", preferencesPath);
        }
    }

    private string GetTrackerConfigPath() => this._pathProvider.GetAuthFilePath();

    private string GetProvidersConfigPath() => this._pathProvider.GetProviderConfigFilePath();

    private string GetPreferencesPath() => this._pathProvider.GetPreferencesFilePath();

    private async Task<Dictionary<string, ProviderConfig>> LoadMergedConfigsAsync()
    {
        var mergedConfigs = new Dictionary<string, ProviderConfig>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in BuildConfigEntries(this._pathProvider))
        {
            await this.MergeConfigFileAsync(
                mergedConfigs,
                entry.Path,
                entry.IsAuthFile).ConfigureAwait(false);
        }

        return mergedConfigs;
    }

    internal static IReadOnlyList<(string Path, bool IsAuthFile)> BuildConfigEntries(IAppPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);

        var entries = new List<(string Path, bool IsAuthFile)>();
        var userProfileRoot = pathProvider.GetUserProfileRoot();
        foreach (var legacyAuthPath in GetLegacyOpenCodeAuthPaths(userProfileRoot))
        {
            entries.Add((legacyAuthPath, true));
        }

        entries.Add((pathProvider.GetProviderConfigFilePath(), false));

        var appDataRoot = pathProvider.GetAppDataRoot();
        if (!string.IsNullOrWhiteSpace(appDataRoot))
        {
            entries.Add((Path.Combine(appDataRoot, AuthConfigFileName), true));
        }

        // App-owned auth file is read last so explicit user-entered keys remain authoritative.
        entries.Add((pathProvider.GetAuthFilePath(), true));

        var distinctEntries = new List<(string Path, bool IsAuthFile)>(entries.Count);
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Path))
            {
                continue;
            }

            if (seenPaths.Add(entry.Path))
            {
                distinctEntries.Add(entry);
            }
        }

        return distinctEntries;
    }

    private static IEnumerable<string> GetLegacyOpenCodeAuthPaths(string? userProfileRoot)
    {
        if (string.IsNullOrWhiteSpace(userProfileRoot))
        {
            yield break;
        }

        // Ordered least-authoritative to most-authoritative (later entries win).
        // ~/.opencode/ is a legacy path with potentially stale keys.
        // ~/.local/share/opencode/ is the active XDG data directory maintained by OpenCode.
        yield return Path.Combine(userProfileRoot, ".opencode", AuthConfigFileName);
        yield return Path.Combine(userProfileRoot, ".config", OpenCodeDirectoryName, AuthConfigFileName);
        yield return Path.Combine(userProfileRoot, "AppData", "Roaming", OpenCodeDirectoryName, AuthConfigFileName);
        yield return Path.Combine(userProfileRoot, "AppData", "Local", OpenCodeDirectoryName, AuthConfigFileName);
        yield return Path.Combine(userProfileRoot, ".local", "share", OpenCodeDirectoryName, AuthConfigFileName);
    }

    private async Task MergeConfigFileAsync(Dictionary<string, ProviderConfig> mergedConfigs, string path, bool isAuthFile)
    {
        var rawConfigs = await JsonConfigFileStore.ReadJsonElementMapAsync(
            path,
            this._logger).ConfigureAwait(false);

        if (rawConfigs == null)
        {
            return;
        }

        foreach (var entry in rawConfigs)
        {
            this.MergeConfigEntry(mergedConfigs, entry, path, isAuthFile);
        }
    }

    private void MergeConfigEntry(
        Dictionary<string, ProviderConfig> mergedConfigs,
        KeyValuePair<string, JsonElement> entry,
        string path,
        bool isAuthFile)
    {
        var providerId = entry.Key;
        if (providerId.Equals("app_settings", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!ProviderMetadataCatalog.TryGet(providerId, out _))
        {
            this._logger.LogDebug(
                "Ignoring unknown provider config entry {ProviderId} from {Path} in strict catalog mode",
                providerId,
                path);
            return;
        }

        var config = GetOrCreateMergedConfig(mergedConfigs, providerId);
        this.ApplyFileConfig(config, entry.Value, providerId, path, isAuthFile);
    }

    private static ProviderConfig GetOrCreateMergedConfig(Dictionary<string, ProviderConfig> mergedConfigs, string providerId)
    {
        if (!mergedConfigs.TryGetValue(providerId, out var config))
        {
            config = new ProviderConfig { ProviderId = providerId };
            mergedConfigs[providerId] = config;
        }

        return config;
    }

    private void ApplyFileConfig(
        ProviderConfig config,
        JsonElement element,
        string providerId,
        string path,
        bool isAuthFile)
    {
        this.ApplyAuthProperties(config, element, providerId, path, isAuthFile);
        this.ApplyDisplayProperties(config, element, providerId, path);
    }

    private void ApplyAuthProperties(
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
                if (!string.IsNullOrEmpty(config.ApiKey) && !string.Equals(config.ApiKey, value, StringComparison.Ordinal))
                {
                    this._logger.LogDebug(
                        "Auth key for {ProviderId} overwritten by {Path} (previous {OldLength} chars -> new {NewLength} chars)",
                        providerId,
                        path,
                        config.ApiKey.Length,
                        value.Length);
                }

                config.ApiKey = value;
                config.AuthSource = AuthSource.FromConfigFile(path);
            }
        }

        if (element.TryGetProperty("base_url", out var urlProp))
        {
            config.BaseUrl = urlProp.GetString() ?? config.BaseUrl;
        }
    }

    private void ApplyDisplayProperties(
        ProviderConfig config,
        JsonElement element,
        string providerId,
        string path)
    {
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
            config.EnabledSubTrays = ReadStringList(subTraysProp);
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
                       CaseInsensitiveOptions)
                   ?? new List<AIModelConfig>();
        }
        catch (Exception ex) when (ex is JsonException)
        {
            this._logger.LogDebug(ex, "Failed to parse model configuration for provider {ProviderId} from {Path}", providerId, path);
            return new List<AIModelConfig>();
        }
    }

    private static List<string> ReadStringList(JsonElement arrayElement)
    {
        return arrayElement.EnumerateArray()
            .Select(item => item.GetString())
            .OfType<string>()
            .ToList();
    }

    private async Task ApplyDiscoveredTokensAsync(List<ProviderConfig> configs)
    {
        var discoveryService = new TokenDiscoveryService(this._log, this._pathProvider);
        var discovered = await discoveryService.DiscoverTokensAsync().ConfigureAwait(false);

        foreach (var discoveredConfig in discovered)
        {
            MergeDiscoveredConfig(configs, discoveredConfig);
        }
    }

    private static void MergeDiscoveredConfig(List<ProviderConfig> configs, ProviderConfig discoveredConfig)
    {
        var existing = configs.FirstOrDefault(config =>
            config.ProviderId.Equals(discoveredConfig.ProviderId, StringComparison.OrdinalIgnoreCase));

        if (existing == null)
        {
            configs.Add(discoveredConfig);
            return;
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
    }

    private static void EnsureParentDirectoryExists(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (directory != null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private async Task<Dictionary<string, object>> LoadExportPayloadAsync(string path)
    {
        return await JsonConfigFileStore.ReadAsync<Dictionary<string, object>>(
                   path,
                   this._logger)
               .ConfigureAwait(false)
               ?? new Dictionary<string, object>(StringComparer.Ordinal);
    }

    private static async Task WriteExportPayloadAsync(string path, Dictionary<string, object> payload)
    {
        await JsonConfigFileStore.WriteIndentedAsync(path, payload).ConfigureAwait(false);
    }
}
