using AIConsumptionTracker.Core.Models;
using System.Collections;
using System.Text.Json;

namespace AIConsumptionTracker.Infrastructure.Configuration;

public class TokenDiscoveryService
{
    public List<ProviderConfig> DiscoverTokens()
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
        }

        // 3. Discover from Kilo Code
        DiscoverKiloCodeTokens(discoveredConfigs);

        // 4. Discover from providers.json (to get IDs user might have added)
        DiscoverFromProvidersFile(discoveredConfigs);

        // 5. Discover from GitHub CLI
        DiscoverGitHubCliToken(discoveredConfigs);

        // 6. Discover from Claude Code
        DiscoverClaudeCodeToken(discoveredConfigs);

        return discoveredConfigs;
    }

    private void DiscoverGitHubCliToken(List<ProviderConfig> configs)
    {
        try
        {
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "gh",
                    Arguments = "auth token",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            process.Start();
            string token = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            if (!string.IsNullOrEmpty(token) && process.ExitCode == 0)
            {
                AddOrUpdate(configs, "github-copilot", token, "Discovered via GitHub CLI", "GitHub CLI Safe Storage");
            }
        }
        catch 
        { 
            // GitHub CLI might not be installed or authenticated
        }
    }

    private void DiscoverClaudeCodeToken(List<ProviderConfig> configs)
    {
        try
        {
            var claudeCredentialsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");
            if (File.Exists(claudeCredentialsPath))
            {
                var json = File.ReadAllText(claudeCredentialsPath);
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
        catch
        {
            // Claude Code might not be installed or credentials file might be corrupted
        }
    }

    private void AddWellKnownProviders(List<ProviderConfig> configs)
    {
        var wellKnown = new[] { 
            "openai", "anthropic", "gemini-cli", "github-copilot", 
            "minimax", "minimax-io", "xiaomi", "kimi", 
            "deepseek", "openrouter", "kilocode", "antigravity", "opencode-zen"
        };
        foreach (var id in wellKnown)
        {
            AddIfNotExists(configs, id, "", "Well-known provider", "System Default");
        }
    }

    private void DiscoverFromProvidersFile(List<ProviderConfig> configs)
    {
        var providersPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "opencode", "providers.json");
        if (File.Exists(providersPath))
        {
            try
            {
                var json = File.ReadAllText(providersPath);
                var known = JsonSerializer.Deserialize<Dictionary<string, string>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (known != null)
                {
                    foreach (var id in known.Keys)
                    {
                        AddIfNotExists(configs, id, "", "Discovered in providers.json", "Config: providers.json");
                    }
                }
            }
            catch { }
        }
    }

    private void AddOrUpdate(List<ProviderConfig> configs, string providerId, string key, string description, string source)
    {
        var existing = configs.FirstOrDefault(c => c.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
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
                Type = "pay-as-you-go",
                Description = description,
                AuthSource = source
            });
        }
    }

    private void DiscoverKiloCodeTokens(List<ProviderConfig> configs)
    {
        var kiloPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kilocode", "secrets.json");
        if (File.Exists(kiloPath))
        {
            try
            {
                var json = File.ReadAllText(kiloPath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("kilo code.kilo-code", out var kiloEntry))
                {
                    // 1. Direct kilocodeToken
                    if (kiloEntry.TryGetProperty("kilocodeToken", out var tokenProp))
                    {
                        var token = tokenProp.GetString();
                        if (!string.IsNullOrEmpty(token))
                        {
                            AddIfNotExists(configs, "kilocode", token, "Discovered in Kilo Code secrets", "Kilo Code Secrets");
                        }
                    }

                    // 2. Nested Roo Cline config
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
                    TryAddRooKey(configs, config, "anthropicApiKey", "anthropic");
                    TryAddRooKey(configs, config, "openAiApiKey", "openai");
                    TryAddRooKey(configs, config, "geminiApiKey", "gemini");
                    TryAddRooKey(configs, config, "openrouterApiKey", "openrouter");
                    TryAddRooKey(configs, config, "mistralApiKey", "mistral");
                    TryAddRooKey(configs, config, "kilocodeToken", "kilocode");
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
            configs.Add(new ProviderConfig
            {
                ProviderId = providerId,
                ApiKey = key,
                Type = "pay-as-you-go",
                Description = description,
                AuthSource = source
            });
        }
    }
}

