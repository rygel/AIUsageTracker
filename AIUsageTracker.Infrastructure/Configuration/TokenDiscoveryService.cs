// <copyright file="TokenDiscoveryService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Collections;
using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Paths;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Configuration;

public class TokenDiscoveryService
{
    private readonly ILogger<TokenDiscoveryService> _logger;
    private readonly IAppPathProvider _pathProvider;

    public TokenDiscoveryService(ILogger<TokenDiscoveryService> logger, IAppPathProvider pathProvider)
    {
        this._logger = logger;
        this._pathProvider = pathProvider;
    }

    private string GetUserProfilePath() => this._pathProvider.GetUserProfileRoot();

    public async Task<IReadOnlyList<ProviderConfig>> DiscoverTokensAsync()
    {
        var discoveredConfigs = new List<ProviderConfig>();

        // 1. Start with well-known supported providers (ensure they show up in --all)
        this.AddWellKnownProviders(discoveredConfigs);

        // 2. Discover from environment variables
        var envVars = Environment.GetEnvironmentVariables();

        foreach (DictionaryEntry var in envVars)
        {
            var key = var.Key.ToString()?.ToUpperInvariant();
            var value = var.Value?.ToString();

            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
            {
                continue;
            }

            this.TryAddEnvironmentVariable(discoveredConfigs, key, value);
        }

        // 3. Discover from Kilo Code
        await this.DiscoverKiloCodeTokensAsync(discoveredConfigs).ConfigureAwait(false);

        // 4. Discover from Roo Code
        await this.DiscoverRooCodeTokensAsync(discoveredConfigs).ConfigureAwait(false);

        // 5. Discover from Claude Code
        await this.DiscoverClaudeCodeTokenAsync(discoveredConfigs).ConfigureAwait(false);

        // 6. Discover native Codex session token
        await this.DiscoverCodexSessionTokenAsync(discoveredConfigs).ConfigureAwait(false);

        return discoveredConfigs;
    }

    private async Task DiscoverCodexSessionTokenAsync(List<ProviderConfig> configs)
    {
        try
        {
            foreach (var path in this.GetCodexAuthCandidates())
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var token = TryReadCodexAccessToken(doc.RootElement);

                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                this.AddOrUpdate(configs, CodexProvider.StaticDefinition.ProviderId, token, "Discovered in Codex auth", $"Config: {path}");
                return;
            }
        }
        catch (Exception ex)
        {
            this._logger.LogDebug("Codex session discovery failed: {Message}", ex.Message);
        }
    }

    private async Task DiscoverClaudeCodeTokenAsync(List<ProviderConfig> configs)
    {
        try
        {
            foreach (var path in this.GetCandidatePaths(ClaudeCodeProvider.StaticDefinition))
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var token = TryReadAccessToken(doc.RootElement, ClaudeCodeProvider.StaticDefinition);
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                this.AddOrUpdate(
                    configs,
                    ClaudeCodeProvider.StaticDefinition.ProviderId,
                    token,
                    "Discovered in Claude Code credentials",
                    $"Claude Code: {path}");
                return;
            }
        }
        catch (Exception ex)
        {
            this._logger.LogDebug("Claude Code discovery failed: {Message}", ex.Message);
        }
    }

    private void AddWellKnownProviders(List<ProviderConfig> configs)
    {
        var wellKnownIds = ProviderMetadataCatalog.Definitions
            .Where(definition => definition.IncludeInWellKnownProviders)
            .Select(definition => definition.ProviderId);

        foreach (var id in wellKnownIds)
        {
            this.AddIfNotExists(configs, id, string.Empty, "Well-known provider", "System Default");
        }
    }

    private IEnumerable<string> GetCodexAuthCandidates()
    {
        return this.GetCandidatePaths(CodexProvider.StaticDefinition);
    }

    private static string? TryReadCodexAccessToken(JsonElement root)
    {
        return TryReadAccessToken(root, CodexProvider.StaticDefinition);
    }

    private static string? TryReadAccessToken(JsonElement root, ProviderDefinition definition)
    {
        foreach (var schema in definition.SessionAuthFileSchemas)
        {
            if (!root.TryGetProperty(schema.RootProperty, out var element) || element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (element.TryGetProperty(schema.AccessTokenProperty, out var accessElement) &&
                accessElement.ValueKind == JsonValueKind.String)
            {
                return accessElement.GetString();
            }
        }

        return null;
    }

    private IEnumerable<string> GetCandidatePaths(ProviderDefinition definition)
    {
        return definition.AuthIdentityCandidatePathTemplates
            .Select(this.ResolvePathTemplate)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)!;
    }

    private string ResolvePathTemplate(string pathTemplate)
    {
        return AuthPathTemplateResolver.Resolve(pathTemplate, this.GetUserProfilePath());
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
                                "Kilo Code Roo Config");
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
                    $"Roo Code: {stateFile}");
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
                    $"Roo Code: {secretsPath}");
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
            $"Env: {environmentVariableName}");
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

        if (!configs.Any(c => c.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase)))
        {
            configs.Add(defaultConfig);
        }
    }
}
