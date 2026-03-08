using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Infrastructure.Providers;
using System.Collections;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Configuration;

public class TokenDiscoveryService
{
    private readonly ILogger<TokenDiscoveryService> _logger;
    private readonly IAppPathProvider _pathProvider;

    public TokenDiscoveryService(ILogger<TokenDiscoveryService> logger, IAppPathProvider pathProvider)
    {
        _logger = logger;
        _pathProvider = pathProvider;
    }

    private string GetUserProfilePath() => this._pathProvider.GetUserProfileRoot();
    private string GetAppDataPath() => Path.Combine(this.GetUserProfilePath(), "AppData", "Roaming");
    private string GetLocalAppDataPath() => Path.Combine(this.GetUserProfilePath(), "AppData", "Local");

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

            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value)) continue;

            this.TryAddEnvironmentVariable(discoveredConfigs, key, value);
        }

        // 3. Discover from Kilo Code
        await this.DiscoverKiloCodeTokensAsync(discoveredConfigs).ConfigureAwait(false);

        // 4. Discover from Roo Code
        await this.DiscoverRooCodeTokensAsync(discoveredConfigs).ConfigureAwait(false);

        // 5. Discover from providers.json (to get IDs user might have added)
        await this.DiscoverFromProvidersFileAsync(discoveredConfigs).ConfigureAwait(false);

        // 6. Discover from Claude Code
        await this.DiscoverClaudeCodeTokenAsync(discoveredConfigs).ConfigureAwait(false);

        // 7. Discover native Codex session token
        await this.DiscoverCodexSessionTokenAsync(discoveredConfigs).ConfigureAwait(false);

        // 8. Discover OpenAI session token from OpenCode auth files
        await this.DiscoverOpenAiSessionTokenAsync(discoveredConfigs).ConfigureAwait(false);

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
            _logger.LogDebug("Codex session discovery failed: {Message}", ex.Message);
        }
    }

    private async Task DiscoverOpenAiSessionTokenAsync(List<ProviderConfig> configs)
    {
        try
        {
            foreach (var path in this.GetOpenCodeAuthCandidates())
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var token = TryReadOpenAiSessionAccessToken(doc.RootElement);
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                // Session-based OpenAI access should be represented by the canonical session provider.
                this.AddOrUpdate(
                    configs,
                    OpenAIProvider.StaticDefinition.SessionAuthCanonicalProviderId ?? CodexProvider.StaticDefinition.ProviderId,
                    token,
                    "Discovered in OpenCode auth",
                    $"Config: {path}");
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("OpenCode session discovery failed: {Message}", ex.Message);
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
            _logger.LogDebug("Claude Code discovery failed: {Message}", ex.Message);
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

    private async Task DiscoverFromProvidersFileAsync(List<ProviderConfig> configs)
    {
        var providersPath = this._pathProvider.GetProviderConfigFilePath();

        _logger.LogDebug("[OpenCode Discovery] Checking for providers.json at: {Path}", providersPath);

        if (File.Exists(providersPath))
        {
            _logger.LogInformation("[OpenCode Discovery] Found providers.json at: {Path}", providersPath);

            try
            {
                var json = await File.ReadAllTextAsync(providersPath).ConfigureAwait(false);
                _logger.LogDebug("[OpenCode Discovery] Read {Length} bytes from providers.json", json.Length);

                var known = JsonSerializer.Deserialize<Dictionary<string, string>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (known != null)
                {
                    _logger.LogInformation("[OpenCode Discovery] Parsed {Count} providers from providers.json", known.Count);

                    foreach (var id in known.Keys)
                    {
                        _logger.LogDebug("[OpenCode Discovery] Found provider: {ProviderId}, Key present: {HasKey}",
                            id, !string.IsNullOrEmpty(known[id]));
                        this.AddIfNotExists(configs, id, known[id], "Discovered in providers.json", "Config: providers.json");
                    }
                }
                else
                {
                    _logger.LogWarning("[OpenCode Discovery] Failed to parse providers.json - result was null");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[OpenCode Discovery] Error reading providers.json: {Message}", ex.Message);
            }
        }
        else
        {
            _logger.LogDebug("[OpenCode Discovery] providers.json not found at: {Path}", providersPath);
        }
    }

    private IEnumerable<string> GetCodexAuthCandidates()
    {
        return this.GetCandidatePaths(CodexProvider.StaticDefinition);
    }

    private IEnumerable<string> GetOpenCodeAuthCandidates()
    {
        return new[] { this._pathProvider.GetAuthFilePath() }
            .Concat(this.GetCandidatePaths(OpenAIProvider.StaticDefinition))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static string? TryReadCodexAccessToken(JsonElement root)
    {
        return TryReadAccessToken(root, CodexProvider.StaticDefinition);
    }

    private static string? TryReadOpenAiSessionAccessToken(JsonElement root)
    {
        return TryReadAccessToken(root, OpenAIProvider.StaticDefinition);
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
        return pathTemplate
            .Replace("%USERPROFILE%", GetUserProfilePath(), StringComparison.OrdinalIgnoreCase)
            .Replace("%APPDATA%", GetAppDataPath(), StringComparison.OrdinalIgnoreCase)
            .Replace("%LOCALAPPDATA%", GetLocalAppDataPath(), StringComparison.OrdinalIgnoreCase);
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
            _logger.LogWarning("Skipping token discovery for unsupported provider id '{ProviderId}'.", providerId);
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
        // 1. Try VS Code extension secrets.json
        var kiloSecretsPath = Path.Combine(this.GetUserProfilePath(), ".kilocode", "secrets.json");
        if (File.Exists(kiloSecretsPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(kiloSecretsPath).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("kilo code.kilo-code", out var kiloEntry))
                {
                    // Extract Roo Cline config for other providers (not Kilo Code itself)
                    if (kiloEntry.TryGetProperty("roo_cline_config_api_config", out var rooProp))
                    {
                        var rooJson = rooProp.GetString();
                        if (!string.IsNullOrEmpty(rooJson))
                        {
                            this.ParseRooConfig(configs, rooJson);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse Kilo Code secrets from {Path}", kiloSecretsPath);
            }
        }
    }

    private async Task DiscoverRooCodeTokensAsync(List<ProviderConfig> configs)
    {
        try
        {
            // Roo Code stores its config in VS Code globalStorage
            var vscodePath = this.GetVSCodeGlobalStoragePath();
            if (!string.IsNullOrEmpty(vscodePath))
            {
                var rooStoragePath = Path.Combine(vscodePath, "roovetgit.roo-code");
                if (Directory.Exists(rooStoragePath))
                {
                    // Look for state.vscdb or similar files
                    var stateFiles = Directory.GetFiles(rooStoragePath, "*.json");
                    foreach (var stateFile in stateFiles)
                    {
                        try
                        {
                            var json = await File.ReadAllTextAsync(stateFile).ConfigureAwait(false);
                            using var doc = JsonDocument.Parse(json);

                            // Extract API configurations from Roo Code state
                             if (doc.RootElement.TryGetProperty("apiConfigs", out var configsProp))
                            {
                                foreach (var configPair in configsProp.EnumerateObject())
                                {
                                    var config = configPair.Value;
                                    TryAddRooKeys(configs, config);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Failed to parse Roo Code state file {Path}", stateFile);
                        }
                    }
                }
            }

            // Also check for standalone Roo Code config directory (similar to Kilo Code)
            var rooConfigPath = Path.Combine(this.GetUserProfilePath(), ".roo");
            if (Directory.Exists(rooConfigPath))
            {
                var secretsPath = Path.Combine(rooConfigPath, "secrets.json");
                if (File.Exists(secretsPath))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(secretsPath).ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(json);

                        // Parse similar structure to Kilo Code
                        if (doc.RootElement.TryGetProperty("roo", out var rooEntry))
                        {
                            if (rooEntry.TryGetProperty("apiConfigs", out var configsProp))
                            {
                                foreach (var configPair in configsProp.EnumerateObject())
                                {
                                    var config = configPair.Value;
                                    this.TryAddRooKeys(configs, config);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to parse Roo secrets from {Path}", secretsPath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Roo Code token discovery failed");
        }
    }

    private string? GetVSCodeGlobalStoragePath()
    {
        try
        {
            // Windows
            if (OperatingSystem.IsWindows())
            {
                var appData = this.GetAppDataPath();
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
            _logger.LogDebug("VS Code config path discovery failed: {Message}", ex.Message);
            return null;
        }
    }


    private void ParseRooConfig(List<ProviderConfig> configs, string rooJson)
    {
        try
        {
            using var rooDoc = JsonDocument.Parse(rooJson);
            if (rooDoc.RootElement.TryGetProperty("apiConfigs", out var configsProp))
            {
                foreach (var configPair in configsProp.EnumerateObject())
                {
                    var config = configPair.Value;

                    // Logic for common providers in Roo Cline
                    this.TryAddRooKeys(configs, config);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse Roo config during token discovery");
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

    private void TryAddRooKeys(List<ProviderConfig> configs, JsonElement config)
    {
        foreach (var definition in ProviderMetadataCatalog.Definitions.Where(d => d.RooConfigPropertyNames.Count > 0))
        {
            foreach (var propertyName in definition.RooConfigPropertyNames)
            {
                this.TryAddRooKey(configs, config, propertyName, definition.ProviderId);
            }
        }
    }

    private void TryAddRooKey(List<ProviderConfig> configs, JsonElement config, string propName, string providerId)
    {
        if (config.TryGetProperty(propName, out var keyProp))
        {
            var key = keyProp.GetString();
            if (!string.IsNullOrEmpty(key))
            {
                this.AddIfNotExists(configs, providerId, key, "Discovered in Kilo Code (Roo Config)", "Kilo Code Roo Config");
            }
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
            _logger.LogDebug("Ignoring unsupported provider id '{ProviderId}' from discovery source '{Source}'.", providerId, source);
            return;
        }

        if (!configs.Any(c => c.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase)))
        {
            configs.Add(defaultConfig);
        }
    }
}
