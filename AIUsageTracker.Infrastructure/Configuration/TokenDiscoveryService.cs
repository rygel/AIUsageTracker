using AIUsageTracker.Core.Models;
using System.Collections;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Configuration;

public class TokenDiscoveryService
{
    private readonly ILogger<TokenDiscoveryService> _logger;

    public TokenDiscoveryService(ILogger<TokenDiscoveryService> logger)
    {
        _logger = logger;
    }

    public async Task<List<ProviderConfig>> DiscoverTokensAsync()
    {
        var discoveredConfigs = new List<ProviderConfig>();
        
        // 1. Start with well-known supported providers (ensure they show up in --all)
        AddWellKnownProviders(discoveredConfigs);

        // 2. Discover from environment variables
        var envVars = Environment.GetEnvironmentVariables();

        foreach (DictionaryEntry var in envVars)
        {
            var key = var.Key.ToString()?.ToUpperInvariant();
            var value = var.Value?.ToString();

            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value)) continue;

            if (key == "MINIMAX_API_KEY")
            {
                AddOrUpdate(discoveredConfigs, "minimax", value, "Discovered via Environment Variable", "Env: MINIMAX_API_KEY");
            }
            else if (key == "XIAOMI_API_KEY" || key == "MIMO_API_KEY")
            {
                AddOrUpdate(discoveredConfigs, "xiaomi", value, "Discovered via Environment Variable", "Env: XIAOMI_API_KEY");
            }
            else if (key == "KIMI_API_KEY" || key == "MOONSHOT_API_KEY")
            {
                AddOrUpdate(discoveredConfigs, "kimi", value, "Discovered via Environment Variable", "Env: KIMI_API_KEY");
            }
            else if (key == "ANTHROPIC_API_KEY" || key == "CLAUDE_API_KEY")
            {
                AddOrUpdate(discoveredConfigs, "claude-code", value, "Discovered via Environment Variable", "Env: ANTHROPIC_API_KEY");
            }
            else if (key == "OPENAI_API_KEY")
            {
                AddOrUpdate(discoveredConfigs, "openai", value, "Discovered via Environment Variable", "Env: OPENAI_API_KEY");
            }
            else if (key == "OPENROUTER_API_KEY")
            {
                AddOrUpdate(discoveredConfigs, "openrouter", value, "Discovered via Environment Variable", "Env: OPENROUTER_API_KEY");
            }
        }

        // 3. Discover from Kilo Code
        await DiscoverKiloCodeTokensAsync(discoveredConfigs);

        // 4. Discover from Roo Code
        await DiscoverRooCodeTokensAsync(discoveredConfigs);

        // 5. Discover from providers.json (to get IDs user might have added)
        await DiscoverFromProvidersFileAsync(discoveredConfigs);

        // 6. Discover from Claude Code
        await DiscoverClaudeCodeTokenAsync(discoveredConfigs);

        // 8. Discover OpenAI session token from OpenCode/Codex auth files
        await DiscoverOpenAiSessionTokenAsync(discoveredConfigs);

        return discoveredConfigs;
    }

    private async Task DiscoverOpenAiSessionTokenAsync(List<ProviderConfig> configs)
    {
        try
        {
            var candidates = new List<string>
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "auth.json"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "opencode", "auth.json"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".opencode", "auth.json")
            };

            if (OperatingSystem.IsWindows())
            {
                candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "codex", "auth.json"));
                candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "opencode", "auth.json"));
                candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "opencode", "auth.json"));
            }

            foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var json = await File.ReadAllTextAsync(path);
                using var doc = JsonDocument.Parse(json);

                string? token = null;
                if (doc.RootElement.TryGetProperty("tokens", out var tokensElement) &&
                    tokensElement.ValueKind == JsonValueKind.Object &&
                    tokensElement.TryGetProperty("access_token", out var accessTokenElement))
                {
                    token = accessTokenElement.GetString();
                }

                if (string.IsNullOrWhiteSpace(token) &&
                    doc.RootElement.TryGetProperty("openai", out var openaiElement) &&
                    openaiElement.ValueKind == JsonValueKind.Object &&
                    openaiElement.TryGetProperty("access", out var openaiAccessElement))
                {
                    token = openaiAccessElement.GetString();
                }

                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                AddOrUpdate(configs, "openai", token, "Discovered in OpenCode/Codex auth", $"Config: {path}");

                // OpenCode/Codex session token represents coding-plan quota tracking.
                if (!token.StartsWith("sk-", StringComparison.OrdinalIgnoreCase))
                {
                    var openAiConfig = configs.FirstOrDefault(c =>
                        c.ProviderId.Equals("openai", StringComparison.OrdinalIgnoreCase));
                    if (openAiConfig != null)
                    {
                        openAiConfig.PlanType = PlanType.Coding;
                        openAiConfig.Type = "quota-based";
                    }
                }
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
            var claudeCredentialsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");
            if (File.Exists(claudeCredentialsPath))
            {
                var json = await File.ReadAllTextAsync(claudeCredentialsPath);
                using var doc = JsonDocument.Parse(json);
                
                if (doc.RootElement.TryGetProperty("claudeAiOauth", out var oauthElement))
                {
                    if (oauthElement.TryGetProperty("accessToken", out var tokenElement))
                    {
                        var token = tokenElement.GetString();
                        if (!string.IsNullOrEmpty(token))
                        {
                            AddOrUpdate(configs, "claude-code", token, "Discovered in Claude Code credentials", "Claude Code: ~/.claude/.credentials.json");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Claude Code discovery failed: {Message}", ex.Message);
        }
    }

    private void AddWellKnownProviders(List<ProviderConfig> configs)
    {
        var wellKnown = new[] { 
            "openai", "gemini-cli", "github-copilot", 
            "minimax", "minimax-io", "xiaomi", "kimi", 
            "deepseek", "openrouter", "antigravity", "opencode"
        };
        foreach (var id in wellKnown)
        {
            AddIfNotExists(configs, id, "", "Well-known provider", "System Default");
        }
    }

    private async Task DiscoverFromProvidersFileAsync(List<ProviderConfig> configs)
    {
        var providersPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "opencode", "providers.json");
        
        _logger.LogDebug("[OpenCode Discovery] Checking for providers.json at: {Path}", providersPath);
        
        if (File.Exists(providersPath))
        {
            _logger.LogInformation("[OpenCode Discovery] Found providers.json at: {Path}", providersPath);
            
            try
            {
                var json = await File.ReadAllTextAsync(providersPath);
                _logger.LogDebug("[OpenCode Discovery] Read {Length} bytes from providers.json", json.Length);
                
                var known = JsonSerializer.Deserialize<Dictionary<string, string>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                if (known != null)
                {
                    _logger.LogInformation("[OpenCode Discovery] Parsed {Count} providers from providers.json", known.Count);
                    
                    foreach (var id in known.Keys)
                    {
                        _logger.LogDebug("[OpenCode Discovery] Found provider: {ProviderId}, Key present: {HasKey}", 
                            id, !string.IsNullOrEmpty(known[id]));
                        AddIfNotExists(configs, id, known[id], "Discovered in providers.json", "Config: providers.json");
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

    private void AddOrUpdate(List<ProviderConfig> configs, string providerId, string key, string description, string source)
    {
        var (planType, type) = GetProviderDefaults(providerId);
        var existing = configs.FirstOrDefault(c => c.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.PlanType = planType;
            existing.Type = type;
            if (!string.IsNullOrEmpty(key))
            {
                existing.ApiKey = key;
                existing.Description = description;
                existing.AuthSource = source;
            }
        }
        else
        {
            configs.Add(new ProviderConfig
            {
                ProviderId = providerId,
                ApiKey = key,
                Type = type,
                PlanType = planType,
                Description = description,
                AuthSource = source
            });
        }
    }

    private async Task DiscoverKiloCodeTokensAsync(List<ProviderConfig> configs)
    {
        // 1. Try VS Code extension secrets.json
        var kiloSecretsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kilocode", "secrets.json");
        if (File.Exists(kiloSecretsPath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(kiloSecretsPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("kilo code.kilo-code", out var kiloEntry))
                {
                    // Extract Roo Cline config for other providers (not Kilo Code itself)
                    if (kiloEntry.TryGetProperty("roo_cline_config_api_config", out var rooProp))
                    {
                        var rooJson = rooProp.GetString();
                        if (!string.IsNullOrEmpty(rooJson))
                        {
                            ParseRooConfig(configs, rooJson);
                        }
                    }
                }
            }
            catch { /* Ignore parse errors */ }
        }
    }

    private async Task DiscoverRooCodeTokensAsync(List<ProviderConfig> configs)
    {
        try
        {
            // Roo Code stores its config in VS Code globalStorage
            var vscodePath = GetVSCodeGlobalStoragePath();
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
                            var json = await File.ReadAllTextAsync(stateFile);
                            using var doc = JsonDocument.Parse(json);
                            
                            // Extract API configurations from Roo Code state
                             if (doc.RootElement.TryGetProperty("apiConfigs", out var configsProp))
                            {
                                foreach (var configPair in configsProp.EnumerateObject())
                                {
                                    var config = configPair.Value;
                                    TryAddRooKey(configs, config, "openAiApiKey", "openai");
                                    TryAddRooKey(configs, config, "geminiApiKey", "gemini");
                                    TryAddRooKey(configs, config, "openrouterApiKey", "openrouter");
                                    TryAddRooKey(configs, config, "mistralApiKey", "mistral");
                                }
                            }
                        }
                        catch { /* Ignore individual file errors */ }
                    }
                }
            }
            
            // Also check for standalone Roo Code config directory (similar to Kilo Code)
            var rooConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".roo");
            if (Directory.Exists(rooConfigPath))
            {
                var secretsPath = Path.Combine(rooConfigPath, "secrets.json");
                if (File.Exists(secretsPath))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(secretsPath);
                        using var doc = JsonDocument.Parse(json);
                        
                        // Parse similar structure to Kilo Code
                        if (doc.RootElement.TryGetProperty("roo", out var rooEntry))
                        {
                            if (rooEntry.TryGetProperty("apiConfigs", out var configsProp))
                            {
                                foreach (var configPair in configsProp.EnumerateObject())
                                {
                                    var config = configPair.Value;
                                    TryAddRooKey(configs, config, "openAiApiKey", "openai");
                                    TryAddRooKey(configs, config, "geminiApiKey", "gemini");
                                    TryAddRooKey(configs, config, "openrouterApiKey", "openrouter");
                                    TryAddRooKey(configs, config, "mistralApiKey", "mistral");
                                }
                            }
                        }
                    }
                    catch { /* Ignore parse errors */ }
                }
            }
        }
        catch { /* Ignore all errors */ }
    }
    
    private string? GetVSCodeGlobalStoragePath()
    {
        try
        {
            // Windows
            if (OperatingSystem.IsWindows())
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "Code", "User", "globalStorage");
            }
            // macOS
            else if (OperatingSystem.IsMacOS())
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.Combine(home, "Library", "Application Support", "Code", "User", "globalStorage");
            }
            // Linux
            else
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
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
                    TryAddRooKey(configs, config, "openAiApiKey", "openai");
                    TryAddRooKey(configs, config, "geminiApiKey", "gemini");
                    TryAddRooKey(configs, config, "openrouterApiKey", "openrouter");
                    TryAddRooKey(configs, config, "mistralApiKey", "mistoral");
                }
            }
        }
        catch { }
    }

    private void TryAddRooKey(List<ProviderConfig> configs, JsonElement config, string propName, string providerId)
    {
        if (config.TryGetProperty(propName, out var keyProp))
        {
            var key = keyProp.GetString();
            if (!string.IsNullOrEmpty(key))
            {
                AddIfNotExists(configs, providerId, key, "Discovered in Kilo Code (Roo Config)", "Kilo Code Roo Config");
            }
        }
    }

    private void AddIfNotExists(List<ProviderConfig> configs, string providerId, string key, string description, string source)
    {
        if (!configs.Any(c => c.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase)))
        {
            var (planType, type) = GetProviderDefaults(providerId);
            configs.Add(new ProviderConfig
            {
                ProviderId = providerId,
                ApiKey = key,
                Type = type,
                PlanType = planType,
                Description = description,
                AuthSource = source
            });
        }
    }

    private static (PlanType PlanType, string Type) GetProviderDefaults(string providerId)
    {
        if (ProviderPlanClassifier.IsCodingPlanProvider(providerId))
        {
            return (PlanType.Coding, "quota-based");
        }

        return (PlanType.Usage, "pay-as-you-go");
    }
}

