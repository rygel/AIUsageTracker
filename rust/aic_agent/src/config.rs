use aic_core::ProviderConfig;
use serde::{Deserialize, Serialize};
use std::path::PathBuf;
use std::sync::Arc;
use tokio::sync::RwLock;
use tracing::{info, error, debug};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AgentConfig {
    pub refresh_interval_minutes: u64,
    pub auto_refresh_enabled: bool,
    pub discovered_providers: Vec<ProviderConfig>,
    /// If true, GitHub token is known to be invalid (403 forbidden) - skip API calls until re-authenticated
    pub github_token_invalid: bool,
}

impl Default for AgentConfig {
    fn default() -> Self {
        Self {
            refresh_interval_minutes: 5,
            auto_refresh_enabled: true,
            discovered_providers: Vec::new(),
            github_token_invalid: false,
        }
    }
}

/// Get the agent config file path
fn get_agent_config_path() -> PathBuf {
    let config_dir = if cfg!(target_os = "windows") {
        std::env::var("APPDATA").ok()
            .map(|p| PathBuf::from(p).join("ai-consumption-tracker"))
    } else {
        std::env::var("HOME").ok()
            .map(|p| PathBuf::from(p).join(".config").join("ai-consumption-tracker"))
    };
    
    config_dir.unwrap_or_else(|| PathBuf::from(".ai-consumption-tracker"))
        .join("agent_config.json")
}

/// Load github_token_invalid flag from disk
pub async fn load_github_token_invalid() -> bool {
    let path = get_agent_config_path();
    if path.exists() {
        if let Ok(content) = tokio::fs::read_to_string(&path).await {
            if let Ok(config) = serde_json::from_str::<AgentConfig>(&content) {
                return config.github_token_invalid;
            }
        }
    }
    false
}

/// Save github_token_invalid flag to disk
pub async fn save_github_token_invalid(invalid: bool) {
    let path = get_agent_config_path();
    if let Some(parent) = path.parent() {
        let _ = tokio::fs::create_dir_all(parent).await;
    }
    
    // Load existing config or create new
    let mut config = if path.exists() {
        if let Ok(content) = tokio::fs::read_to_string(&path).await {
            serde_json::from_str(&content).unwrap_or_default()
        } else {
            AgentConfig::default()
        }
    } else {
        AgentConfig::default()
    };
    
    config.github_token_invalid = invalid;
    
    if let Ok(json) = serde_json::to_string_pretty(&config) {
        let _ = tokio::fs::write(&path, json).await;
    }
}

/// Perform centralized provider discovery including environment scanning and well-known providers
pub async fn discover_all_providers() -> Vec<ProviderConfig> {
    let mut providers = Vec::new();
    
    // Add well-known providers (matching C# application)
    let well_known = vec![
        ("openai", "OpenAI", false),
        ("claude-code", "Claude Code", false),
        ("gemini-cli", "Google Gemini", false),
        ("github-copilot", "GitHub Copilot", true),  // System provider - no API key needed
        ("minimax", "MiniMax", false),
        ("minimax-io", "MiniMax IO", false),
        ("xiaomi", "Xiaomi", false),
        ("kimi", "Kimi", false),
        ("deepseek", "DeepSeek", false),
        ("openrouter", "OpenRouter", false),
        ("antigravity", "Antigravity", true),  // System provider - discovers running process
        ("opencode", "OpenCode", false),  // API-based provider
        ("mistral", "Mistral", false),
        ("codex", "Codex", true),  // System provider
        ("zai-coding-plan", "Z.ai Coding Plan", false),
    ];
    
    for (id, name, is_system) in well_known {
        let (description, auth_source) = if is_system {
            (format!("{} - Discovers automatically", name), "System".to_string())
        } else {
            (format!("{} - No API key configured", name), "None".to_string())
        };
        
        providers.push(ProviderConfig {
            provider_id: id.to_string(),
            api_key: String::new(),
            config_type: "pay-as-you-go".to_string(),
            description: Some(description),
            auth_source,
            ..Default::default()
        });
    }
    
    // Discover from environment variables
    discover_from_env(&mut providers);
    
    // Discover from config files (cross-platform)
    discover_from_config_files(&mut providers).await;
    
    // Discover GitHub tokens from common locations
    discover_github_token(&mut providers).await;
    
    info!("Discovered {} providers", providers.len());
    providers
}

fn discover_from_env(providers: &mut Vec<ProviderConfig>) {
    // OpenAI
    if let Ok(key) = std::env::var("OPENAI_API_KEY") {
        if !key.is_empty() {
            add_or_update_provider(providers, "openai", &key, "Environment");
        }
    }
    
    // Anthropic / Claude
    if let Ok(key) = std::env::var("ANTHROPIC_API_KEY") {
        if !key.is_empty() {
            add_or_update_provider(providers, "claude-code", &key, "Environment");
        }
    } else if let Ok(key) = std::env::var("CLAUDE_API_KEY") {
        if !key.is_empty() {
            add_or_update_provider(providers, "claude-code", &key, "Environment");
        }
    }
    
    // Google Gemini
    if let Ok(key) = std::env::var("GEMINI_API_KEY") {
        if !key.is_empty() {
            add_or_update_provider(providers, "gemini-cli", &key, "Environment");
        }
    } else if let Ok(key) = std::env::var("GOOGLE_API_KEY") {
        if !key.is_empty() {
            add_or_update_provider(providers, "gemini-cli", &key, "Environment");
        }
    }
    
    // DeepSeek
    if let Ok(key) = std::env::var("DEEPSEEK_API_KEY") {
        if !key.is_empty() {
            add_or_update_provider(providers, "deepseek", &key, "Environment");
        }
    }
    
    // Kimi / Moonshot
    if let Ok(key) = std::env::var("KIMI_API_KEY") {
        if !key.is_empty() {
            add_or_update_provider(providers, "kimi", &key, "Environment");
        }
    } else if let Ok(key) = std::env::var("MOONSHOT_API_KEY") {
        if !key.is_empty() {
            add_or_update_provider(providers, "kimi", &key, "Environment");
        }
    }
    
    // MiniMax
    if let Ok(key) = std::env::var("MINIMAX_API_KEY") {
        if !key.is_empty() {
            add_or_update_provider(providers, "minimax", &key, "Environment");
        }
    }
    
    // Xiaomi
    if let Ok(key) = std::env::var("XIAOMI_API_KEY") {
        if !key.is_empty() {
            add_or_update_provider(providers, "xiaomi", &key, "Environment");
        }
    }
    
    // Antigravity
    if let Ok(key) = std::env::var("ANTIGRAVITY_API_KEY") {
        if !key.is_empty() {
            add_or_update_provider(providers, "antigravity", &key, "Environment");
        }
    }
    
    // OpenRouter
    if let Ok(key) = std::env::var("OPENROUTER_API_KEY") {
        if !key.is_empty() {
            add_or_update_provider(providers, "openrouter", &key, "Environment");
        }
    }
    
    // Z.ai
    if let Ok(key) = std::env::var("ZAI_API_KEY") {
        if !key.is_empty() {
            add_or_update_provider(providers, "zai", &key, "Environment");
        }
    }
}

async fn discover_from_config_files(providers: &mut Vec<ProviderConfig>) {
    // Get home directory (cross-platform)
    let home = if cfg!(target_os = "windows") {
        std::env::var("USERPROFILE").ok()
    } else {
        std::env::var("HOME").ok()
    };
    
    if let Some(home) = home {
        // Tier 1: OpenCode (highest priority)
        info!("Tier 1: Checking OpenCode configuration files...");
        check_config_file_tier1(providers, &format!("{}/.local/share/opencode/auth.json", home), "OpenCode").await;
        check_config_file_tier1(providers, &format!("{}/.config/opencode/auth.json", home), "OpenCode").await;
        check_config_file_tier1(providers, &format!("{}/.opencode/auth.json", home), "OpenCode").await;
        
        #[cfg(target_os = "windows")]
        {
            check_config_file_tier1(providers, &format!("{}\\AppData\\Local\\opencode\\auth.json", home), "OpenCode").await;
            check_config_file_tier1(providers, &format!("{}\\AppData\\Roaming\\opencode\\auth.json", home), "OpenCode").await;
            check_config_file_tier1(providers, &format!("{}\\.opencode\\auth.json", home), "OpenCode").await;
        }
        
        // Tier 2: KiloCode (second priority)
        info!("Tier 2: Checking KiloCode configuration files...");
        check_config_file_tier2(providers, &format!("{}/.local/share/kilocode/auth.json", home), "KiloCode").await;
        check_config_file_tier2(providers, &format!("{}/.config/kilocode/auth.json", home), "KiloCode").await;
        check_config_file_tier2(providers, &format!("{}/.kilocode/auth.json", home), "KiloCode").await;
        
        #[cfg(target_os = "windows")]
        {
            check_config_file_tier2(providers, &format!("{}\\AppData\\Local\\kilocode\\auth.json", home), "KiloCode").await;
            check_config_file_tier2(providers, &format!("{}\\AppData\\Roaming\\kilocode\\auth.json", home), "KiloCode").await;
            check_config_file_tier2(providers, &format!("{}\\.kilocode\\auth.json", home), "KiloCode").await;
        }
        
        // Tier 3: AI Consumption Tracker (lowest priority for config files)
        info!("Tier 3: Checking AI Consumption Tracker configuration files...");
        check_config_file_tier3(providers, &format!("{}/.ai-consumption-tracker/auth.json", home), "AI Consumption Tracker").await;
        check_config_file_tier3(providers, &format!("{}/.local/share/ai-consumption-tracker/auth.json", home), "AI Consumption Tracker").await;
        
        #[cfg(target_os = "windows")]
        {
            check_config_file_tier3(providers, &format!("{}\\.ai-consumption-tracker\\auth.json", home), "AI Consumption Tracker").await;
            check_config_file_tier3(providers, &format!("{}\\AppData\\Local\\ai-consumption-tracker\\auth.json", home), "AI Consumption Tracker").await;
            check_config_file_tier3(providers, &format!("{}\\AppData\\Roaming\\ai-consumption-tracker\\auth.json", home), "AI Consumption Tracker").await;
        }
    }
}

/// Tier 1: OpenCode config files - highest priority, can override any provider except antigravity
async fn check_config_file_tier1(providers: &mut Vec<ProviderConfig>, path: &str, source_name: &str) {
    debug!("Tier 1: Checking config file: {}", path);
    if let Ok(content) = tokio::fs::read_to_string(path).await {
        info!("Tier 1: Found config file: {} (source: {})", path, source_name);
        if let Ok(raw_configs) = serde_json::from_str::<serde_json::Value>(&content) {
            if let Some(obj) = raw_configs.as_object() {
                for (provider_id, value) in obj {
                    // Skip app_settings and antigravity
                    if provider_id == "app_settings" || provider_id == "antigravity" {
                        continue;
                    }
                    
                    if let Some(api_key) = value.get("key").and_then(|v| v.as_str()) {
                        if !api_key.is_empty() {
                            // Tier 1 can add or update any provider
                            add_or_update_provider(providers, provider_id, api_key, source_name);
                            info!("Tier 1: Loaded API key for {} from {} config file", provider_id, source_name);
                        }
                    }
                }
            }
        }
    }
}

/// Tier 2: KiloCode config files - second priority, can add keys for providers not in Tier 1
async fn check_config_file_tier2(providers: &mut Vec<ProviderConfig>, path: &str, source_name: &str) {
    debug!("Tier 2: Checking config file: {}", path);
    if let Ok(content) = tokio::fs::read_to_string(path).await {
        info!("Tier 2: Found config file: {} (source: {})", path, source_name);
        if let Ok(raw_configs) = serde_json::from_str::<serde_json::Value>(&content) {
            if let Some(obj) = raw_configs.as_object() {
                for (provider_id, value) in obj {
                    // Skip app_settings and antigravity
                    if provider_id == "app_settings" || provider_id == "antigravity" {
                        continue;
                    }
                    
                    if let Some(api_key) = value.get("key").and_then(|v| v.as_str()) {
                        if !api_key.is_empty() {
                            // Tier 2: Only add if provider doesn't already have an API key from Tier 1 (OpenCode)
                            let has_tier1_key = providers.iter().any(|p| {
                                p.provider_id == *provider_id 
                                    && !p.api_key.is_empty() 
                                    && p.auth_source == "OpenCode"
                            });
                            
                            if !has_tier1_key {
                                add_or_update_provider(providers, provider_id, api_key, source_name);
                                info!("Tier 2: Loaded API key for {} from {} config file", provider_id, source_name);
                            } else {
                                debug!("Tier 2: Skipping {} - already has key from OpenCode", provider_id);
                            }
                        }
                    }
                }
            }
        }
    }
}

/// Tier 3: Application config files - lowest priority, only adds keys for providers without Tier 1 or Tier 2 keys
async fn check_config_file_tier3(providers: &mut Vec<ProviderConfig>, path: &str, source_name: &str) {
    debug!("Tier 3: Checking config file: {}", path);
    if let Ok(content) = tokio::fs::read_to_string(path).await {
        info!("Tier 3: Found config file: {} (source: {})", path, source_name);
        if let Ok(raw_configs) = serde_json::from_str::<serde_json::Value>(&content) {
            if let Some(obj) = raw_configs.as_object() {
                for (provider_id, value) in obj {
                    // Skip app_settings and antigravity
                    if provider_id == "app_settings" || provider_id == "antigravity" {
                        continue;
                    }
                    
                    if let Some(api_key) = value.get("key").and_then(|v| v.as_str()) {
                        if !api_key.is_empty() {
                            // Tier 3: Only add if provider doesn't have API key from Tier 1 or Tier 2
                            let has_higher_tier_key = providers.iter().any(|p| {
                                p.provider_id == *provider_id 
                                    && !p.api_key.is_empty() 
                                    && (p.auth_source == "OpenCode" || p.auth_source == "KiloCode")
                            });
                            
                            if !has_higher_tier_key {
                                add_or_update_provider(providers, provider_id, api_key, source_name);
                                info!("Tier 3: Loaded API key for {} from {} config file", provider_id, source_name);
                            } else {
                                debug!("Tier 3: Skipping {} - already has key from higher tier", provider_id);
                            }
                        }
                    }
                }
            }
        }
    }
}

/// Legacy function - kept for backward compatibility, delegates to tier1 behavior
async fn check_config_file(providers: &mut Vec<ProviderConfig>, path: &str, source_name: &str) {
    check_config_file_tier1(providers, path, source_name).await;
}

/// Discover GitHub tokens from common locations
async fn discover_github_token(providers: &mut Vec<ProviderConfig>) {
    // First check environment variables
    let env_vars = ["GITHUB_TOKEN", "GH_TOKEN", "GITHUB_COPILOT_TOKEN", "COPILOT_TOKEN"];
    for var in env_vars.iter() {
        if let Ok(token) = std::env::var(var) {
            if !token.is_empty() && token.len() > 10 {
                info!("Found GitHub token in env var {}", var);
                if let Some(provider) = providers.iter_mut().find(|p| p.provider_id == "github-copilot") {
                    provider.api_key = token.clone();
                    provider.auth_source = format!("Environment Variable ({})", var);
                    provider.description = Some("GitHub Copilot - Token discovered from environment".to_string());
                }
                return; // Found a token, no need to check more
            }
        }
    }

    // Try running `gh auth token` command (like C# version does)
    #[cfg(target_os = "windows")]
    {
        match std::process::Command::new("cmd")
            .args(["/C", "gh auth token"])
            .output()
        {
            Ok(output) if output.status.success() => {
                let token = String::from_utf8_lossy(&output.stdout).trim().to_string();
                if !token.is_empty() && token.len() > 10 {
                    info!("Found GitHub token via 'gh auth token' command");
                    if let Some(provider) = providers.iter_mut().find(|p| p.provider_id == "github-copilot") {
                        provider.api_key = token;
                        provider.auth_source = "GitHub CLI".to_string();
                        provider.description = Some("GitHub Copilot - Token discovered from GitHub CLI".to_string());
                    }
                    return;
                }
            }
            Ok(output) => {
                debug!("gh auth token failed: {:?}", String::from_utf8_lossy(&output.stderr));
            }
            Err(e) => {
                debug!("Could not run gh auth token: {}", e);
            }
        }
    }

    #[cfg(not(target_os = "windows"))]
    {
        match std::process::Command::new("sh")
            .args(["-c", "gh auth token"])
            .output()
        {
            Ok(output) if output.status.success() => {
                let token = String::from_utf8_lossy(&output.stdout).trim().to_string();
                if !token.is_empty() && token.len() > 10 {
                    info!("Found GitHub token via 'gh auth token' command");
                    if let Some(provider) = providers.iter_mut().find(|p| p.provider_id == "github-copilot") {
                        provider.api_key = token;
                        provider.auth_source = "GitHub CLI".to_string();
                        provider.description = Some("GitHub Copilot - Token discovered from GitHub CLI".to_string());
                    }
                    return;
                }
            }
            Ok(output) => {
                debug!("gh auth token failed: {:?}", String::from_utf8_lossy(&output.stderr));
            }
            Err(e) => {
                debug!("Could not run gh auth token: {}", e);
            }
        }
    }

    // Get home directory (cross-platform)
    let home = if cfg!(target_os = "windows") {
        std::env::var("USERPROFILE").ok()
    } else {
        std::env::var("HOME").ok()
    };
    
    if let Some(home) = home {
        let mut paths_to_check = vec![
            format!("{}/.config/gh/hosts.yml", home),
            format!("{}/.git-credential-store", home),
            format!("{}/.config/gh/config.yml", home),
            format!("{}/.config/gh/credentials.yml", home),
            format!("{}/.gh", home),
        ];

        // Add VS Code paths for Windows
        #[cfg(target_os = "windows")]
        {
            if let Ok(appdata) = std::env::var("APPDATA") {
                paths_to_check.push(format!("{}\\GitHub CLI\\config.yml", appdata));
            }
            if let Ok(localappdata) = std::env::var("LOCALAPPDATA") {
                paths_to_check.push(format!("{}\\Programs\\Microsoft VS Code\\User\\settings.json", localappdata));
                paths_to_check.push(format!("{}\\Code\\User\\settings.json", localappdata));
            }
        }

        // Windows GitHub CLI paths
        #[cfg(target_os = "windows")]
        {
            paths_to_check.push(format!("{}\\AppData\\Local\\GitHub CLI\\config.yml", home));
            paths_to_check.push(format!("{}\\AppData\\Roaming\\GitHub CLI\\config.yml", home));
        }
        
        for path in paths_to_check.iter() {
            debug!("Checking for GitHub token in: {}", path);
            if let Ok(content) = tokio::fs::read_to_string(path).await {
                if let Some(token) = extract_github_pat(&content) {
                    info!("Found GitHub token in {}", path);
                    let token_len = token.len();
                    // Update github-copilot provider with the token
                    if let Some(provider) = providers.iter_mut().find(|p| p.provider_id == "github-copilot") {
                        provider.api_key = token;
                        provider.auth_source = format!("GitHub CLI ({})", token_len);
                        provider.description = Some("GitHub Copilot - Token discovered from GitHub CLI".to_string());
                    }
                    break; // Found a token, no need to check other files
                }
            }
        }
    }
}

/// Extract GitHub PAT from content
fn extract_github_pat(content: &str) -> Option<String> {
    // Look for github_pat_ tokens (GitHub personal access tokens)
    if let Some(start) = content.find("github_pat_") {
        let rest = &content[start..];
        // Find the end of the token (alphanumeric, underscore, hyphen)
        let end = rest.find(|c: char| !c.is_alphanumeric() && c != '_' && c != '-')
            .unwrap_or(rest.len());
        let token = &rest[..end];
        if token.len() > 10 { // Ensure it's a reasonable token length
            return Some(token.to_string());
        }
    }
    
    // Look for oauth_token in YAML format
    if content.contains("oauth_token:") {
        for line in content.lines() {
            if line.contains("oauth_token:") {
                if let Some(token) = line.split("oauth_token:").nth(1) {
                    let token = token.trim().trim_matches('"').trim_matches('\'');
                    if !token.is_empty() && token.len() > 10 {
                        return Some(token.to_string());
                    }
                }
            }
        }
    }

    // Look for token in various YAML formats
    // Example: token: gho_xxxx or ghp_xxxx
    for line in content.lines() {
        let line = line.trim();
        if line.starts_with("token:") {
            if let Some(token) = line.split(':').nth(1) {
                let token = token.trim().trim_matches('"').trim_matches('\'');
                if !token.is_empty() && token.len() > 10 && (token.starts_with("gho_") || token.starts_with("ghp_") || token.starts_with("ghs_")) {
                    return Some(token.to_string());
                }
            }
        }
    }
    
    // Look for access_token in JSON-like format
    if let Some(start) = content.find("\"access_token\"") {
        let rest = &content[start..];
        if let Some(quote_start) = rest.find('"') {
            let rest = &rest[quote_start+1..];
            if let Some(quote_end) = rest.find('"') {
                let token = &rest[..quote_end];
                if !token.is_empty() && token.len() > 10 {
                    return Some(token.to_string());
                }
            }
        }
    }
    
    None
}

fn add_or_update_provider(
    providers: &mut Vec<ProviderConfig>,
    provider_id: &str,
    api_key: &str,
    source: &str,
) {
    if let Some(existing) = providers.iter_mut().find(|p| p.provider_id == provider_id) {
        if !api_key.is_empty() {
            existing.api_key = api_key.to_string();
            existing.auth_source = format!("{} ({})", source, api_key.chars().count());
        }
    } else {
        providers.push(ProviderConfig {
            provider_id: provider_id.to_string(),
            api_key: api_key.to_string(),
            config_type: "api".to_string(),
            description: Some(format!("Discovered via {}", source)),
            auth_source: source.to_string(),
            ..Default::default()
        });
    }
}

