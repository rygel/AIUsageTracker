// <copyright file="TokenDiscoveryService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Collections;
using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Configuration;

public class TokenDiscoveryService
{
    private static readonly IReadOnlyList<IProviderAuthFallbackResolver> ExplicitProviderFallbackResolvers =
        BuildExplicitProviderFallbackResolvers();

    private readonly ILogger<TokenDiscoveryService> _logger;
    private readonly IAppPathProvider _pathProvider;
    private readonly IReadOnlyList<ProviderSessionTokenResolver> _sessionResolvers;

    public TokenDiscoveryService(ILogger<TokenDiscoveryService> logger, IAppPathProvider pathProvider)
    {
        this._logger = logger;
        this._pathProvider = pathProvider;
        this._sessionResolvers = BuildSessionResolvers(this._logger, this._pathProvider);
    }

    public async Task<IReadOnlyList<ProviderConfig>> DiscoverTokensAsync()
    {
        var discoveredConfigs = new List<ProviderConfig>();
        var environmentVariables = this.GetNormalizedEnvironmentVariables();

        // 1. Start with well-known supported providers (ensure they show up in --all)
        this.AddWellKnownProviders(discoveredConfigs);

        // 2. Discover from environment variables
        foreach (var entry in environmentVariables)
        {
            this.TryAddEnvironmentVariable(discoveredConfigs, entry.Key, entry.Value);
        }

        // 3. Discover from Kilo Code
        await this.DiscoverKiloCodeTokensAsync(discoveredConfigs).ConfigureAwait(false);

        // 4. Discover from Roo Code
        await this.DiscoverRooCodeTokensAsync(discoveredConfigs).ConfigureAwait(false);

        // 5. Apply explicit provider fallback order (env -> provider-specific Roo/Kilo)
        this.ApplyExplicitProviderFallbacks(discoveredConfigs, environmentVariables);

        // 6. Discover provider-specific session tokens
        await this.DiscoverSessionTokensAsync(discoveredConfigs).ConfigureAwait(false);

        return discoveredConfigs;
    }

    private static IReadOnlyList<IProviderAuthFallbackResolver> BuildExplicitProviderFallbackResolvers()
    {
        return ProviderMetadataCatalog.GetProviderIdsWithDiscoveryEnvironmentVariables()
            .Select(providerId => (IProviderAuthFallbackResolver)new ProviderAuthFallbackResolver(
                providerId,
                ProviderMetadataCatalog.GetDiscoveryEnvironmentVariables(providerId).ToArray()))
            .ToArray();
    }

    private static IReadOnlyList<ProviderSessionTokenResolver> BuildSessionResolvers(
        ILogger<TokenDiscoveryService> logger,
        IAppPathProvider pathProvider)
    {
        return ProviderMetadataCatalog.GetProviderIdsWithDedicatedSessionAuthFiles()
            .Select(providerId => new ProviderSessionTokenResolver(
                discoverySpec: ProviderMetadataCatalog.Find(providerId)!.CreateAuthDiscoverySpec(),
                description: $"Discovered in {ProviderMetadataCatalog.GetDisplayName(providerId)} session auth",
                sourcePrefix: "Config",
                logger: logger,
                pathProvider: pathProvider))
            .ToArray();
    }

    private string GetUserProfilePath() => this._pathProvider.GetUserProfileRoot();

    private IReadOnlyDictionary<string, string> GetNormalizedEnvironmentVariables()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var envVars = Environment.GetEnvironmentVariables();
        foreach (DictionaryEntry entry in envVars)
        {
            var key = entry.Key.ToString();
            var value = entry.Value?.ToString();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            result[key.ToUpperInvariant()] = value;
        }

        return result;
    }

    private void ApplyExplicitProviderFallbacks(
        List<ProviderConfig> discoveredConfigs,
        IReadOnlyDictionary<string, string> environmentVariables)
    {
        foreach (var resolver in ExplicitProviderFallbackResolvers)
        {
            var resolved = resolver.Resolve(environmentVariables, discoveredConfigs);
            if (resolved == null)
            {
                continue;
            }

            this.AddOrUpdate(
                discoveredConfigs,
                resolved.ProviderId,
                resolved.ApiKey,
                resolved.Description ?? "Discovered via explicit provider fallback",
                resolved.AuthSource);
        }
    }

    private async Task DiscoverSessionTokensAsync(List<ProviderConfig> configs)
    {
        foreach (var resolver in this._sessionResolvers)
        {
            var resolved = await resolver.TryResolveAsync().ConfigureAwait(false);
            if (resolved == null)
            {
                continue;
            }

            this.AddOrUpdate(
                configs,
                resolved.ProviderId,
                resolved.ApiKey,
                resolved.Description,
                resolved.AuthSource);
        }
    }

    private void AddWellKnownProviders(List<ProviderConfig> configs)
    {
        foreach (var id in ProviderMetadataCatalog.GetWellKnownProviderIds())
        {
            this.AddIfNotExists(configs, id, string.Empty, "Well-known provider", AuthSource.SystemDefault);
        }
    }

    private void AddOrUpdate(List<ProviderConfig> configs, string providerId, string key, string description, string source)
    {
        if (!ProviderMetadataCatalog.TryCreateDefaultConfig(
                providerId,
                out var defaultConfig,
                apiKey: key,
                authSource: source,
                description: description))
        {
            this._logger.LogWarning("Skipping token discovery for unsupported provider id '{ProviderId}'.", providerId);
            return;
        }

        var existing = configs.FirstOrDefault(c => c.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.PlanType = defaultConfig.PlanType;
            existing.Type = defaultConfig.Type;
            if (!string.IsNullOrEmpty(key))
            {
                existing.ApiKey = key;
                existing.Description = description;
                existing.AuthSource = source;
            }
        }
        else
        {
            configs.Add(defaultConfig);
        }
    }

    private async Task DiscoverKiloCodeTokensAsync(List<ProviderConfig> configs)
    {
        var kiloSecretsPath = Path.Combine(this.GetUserProfilePath(), ".kilocode", "secrets.json");
        if (File.Exists(kiloSecretsPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(kiloSecretsPath).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("kilo code.kilo-code", out var kiloEntry))
                {
                    if (kiloEntry.TryGetProperty("roo_cline_config_api_config", out var rooProp))
                    {
                        var rooJson = rooProp.GetString();
                        if (!string.IsNullOrEmpty(rooJson))
                        {
                            this.TryProcessRooConfigJson(
                                configs,
                                rooJson,
                                "Discovered in Kilo Code Roo config",
                                $"{AuthSource.KiloPrefix} Roo Config");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this._logger.LogDebug(ex, "Failed to parse Kilo Code secrets from {Path}", kiloSecretsPath);
            }
        }
    }

    private async Task DiscoverRooCodeTokensAsync(List<ProviderConfig> configs)
    {
        try
        {
            await this.DiscoverRooStateTokensAsync(configs).ConfigureAwait(false);
            await this.DiscoverStandaloneRooTokensAsync(configs).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this._logger.LogDebug(ex, "Roo Code token discovery failed");
        }
    }

    private async Task DiscoverRooStateTokensAsync(List<ProviderConfig> configs)
    {
        var vscodePath = this.GetVSCodeGlobalStoragePath();
        if (string.IsNullOrEmpty(vscodePath))
        {
            return;
        }

        var rooStoragePath = Path.Combine(vscodePath, "roovetgit.roo-code");
        if (!Directory.Exists(rooStoragePath))
        {
            return;
        }

        var stateFiles = Directory.GetFiles(rooStoragePath, "*.json");
        foreach (var stateFile in stateFiles)
        {
            try
            {
                var json = await File.ReadAllTextAsync(stateFile).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                this.TryProcessRooApiConfigs(
                    configs,
                    doc.RootElement,
                    "Discovered in Roo Code state",
                    AuthSource.FromRooPath(stateFile));
            }
            catch (Exception ex)
            {
                this._logger.LogDebug(ex, "Failed to parse Roo Code state file {Path}", stateFile);
            }
        }
    }

    private async Task DiscoverStandaloneRooTokensAsync(List<ProviderConfig> configs)
    {
        var rooConfigPath = Path.Combine(this.GetUserProfilePath(), ".roo");
        if (!Directory.Exists(rooConfigPath))
        {
            return;
        }

        var secretsPath = Path.Combine(rooConfigPath, "secrets.json");
        if (!File.Exists(secretsPath))
        {
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(secretsPath).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("roo", out var rooEntry))
            {
                this.TryProcessRooApiConfigs(
                    configs,
                    rooEntry,
                    "Discovered in Roo Code secrets",
                    AuthSource.FromRooPath(secretsPath));
            }
        }
        catch (Exception ex)
        {
            this._logger.LogDebug(ex, "Failed to parse Roo secrets from {Path}", secretsPath);
        }
    }

    private string? GetVSCodeGlobalStoragePath()
    {
        try
        {
            // Windows
            if (OperatingSystem.IsWindows())
            {
                var appData = Path.Combine(this.GetUserProfilePath(), "AppData", "Roaming");
                return Path.Combine(appData, "Code", "User", "globalStorage");
            }

            // macOS
            else if (OperatingSystem.IsMacOS())
            {
                var home = this.GetUserProfilePath();
                return Path.Combine(home, "Library", "Application Support", "Code", "User", "globalStorage");
            }

            // Linux
            else
            {
                var home = this.GetUserProfilePath();
                var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(home, ".config");
                return Path.Combine(configHome, "Code", "User", "globalStorage");
            }
        }
        catch (Exception ex)
        {
            this._logger.LogDebug("VS Code config path discovery failed: {Message}", ex.Message);
            return null;
        }
    }

    private void TryProcessRooConfigJson(List<ProviderConfig> configs, string rooJson, string description, string source)
    {
        try
        {
            this.AddRooTokens(configs, RooTokenConfigParser.Parse(rooJson), description, source);
        }
        catch (Exception ex)
        {
            this._logger.LogDebug(ex, "Failed to parse Roo config during token discovery");
        }
    }

    private void TryAddEnvironmentVariable(List<ProviderConfig> configs, string environmentVariableName, string value)
    {
        var definition = ProviderMetadataCatalog.FindByEnvironmentVariable(environmentVariableName);
        if (definition == null)
        {
            return;
        }

        this.AddOrUpdate(
            configs,
            definition.ProviderId,
            value,
            "Discovered via Environment Variable",
            AuthSource.FromEnvironmentVariable(environmentVariableName));
    }

    private void TryProcessRooApiConfigs(List<ProviderConfig> configs, JsonElement root, string description, string source)
    {
        this.AddRooTokens(configs, RooTokenConfigParser.Parse(root), description, source);
    }

    private void AddRooTokens(
        List<ProviderConfig> configs,
        IReadOnlyList<RooTokenConfigParser.DiscoveredProviderToken> tokens,
        string description,
        string source)
    {
        foreach (var token in tokens)
        {
            this.AddIfNotExists(configs, token.ProviderId, token.ApiKey, description, source);
        }
    }

    private void AddIfNotExists(List<ProviderConfig> configs, string providerId, string key, string description, string source)
    {
        if (!ProviderMetadataCatalog.TryCreateDefaultConfig(
                providerId,
                out var defaultConfig,
                apiKey: key,
                authSource: source,
                description: description))
        {
            this._logger.LogDebug("Ignoring unsupported provider id '{ProviderId}' from discovery source '{Source}'.", providerId, source);
            return;
        }

        var existing = configs.FirstOrDefault(c => c.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            configs.Add(defaultConfig);
            return;
        }

        if (string.IsNullOrWhiteSpace(existing.ApiKey) && !string.IsNullOrWhiteSpace(key))
        {
            existing.ApiKey = key;
            existing.AuthSource = source;
            existing.Description = description;
            existing.Type = defaultConfig.Type;
            existing.PlanType = defaultConfig.PlanType;
        }
    }
}
