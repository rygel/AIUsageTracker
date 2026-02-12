use aic_core::ProviderConfig;
use serde::{Deserialize, Serialize};
use std::sync::Arc;
use tokio::sync::RwLock;
use tracing::{info, error, debug};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AgentConfig {
    pub refresh_interval_minutes: u64,
    pub auto_refresh_enabled: bool,
    pub discovered_providers: Vec<ProviderConfig>,
}

/// Perform centralized provider discovery including environment scanning and well-known providers
pub async fn discover_all_providers() -> Vec<ProviderConfig> {
    let mut providers = Vec::new();
    
    // Add well-known providers (matching C# application)
    let well_known = vec![
        ("openai", "OpenAI", false),
        ("claude-code", "Anthropic", false),
        ("gemini-cli", "Google Gemini", false),
        ("github-copilot", "GitHub Copilot", true),  // System provider - no API key needed
        ("minimax", "MiniMax", false),
        ("minimax-io", "MiniMax IO", false),
        ("xiaomi", "Xiaomi", false),
        ("kimi", "Kimi", false),
        ("deepseek", "DeepSeek", false),
        ("openrouter", "OpenRouter", false),
        ("antigravity", "Antigravity", true),  // System provider - discovers running process
        ("opencode-zen", "OpenCode", true),  // System provider - OpenCode Zen
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
        // Check specific config file locations with their exact source names
        // OpenCode
        check_config_file(providers, &format!("{}/.local/share/opencode/auth.json", home), "OpenCode").await;
        check_config_file(providers, &format!("{}/.config/opencode/auth.json", home), "OpenCode").await;
        check_config_file(providers, &format!("{}/.opencode/auth.json", home), "OpenCode").await;
        
        // AI Consumption Tracker (this app)
        check_config_file(providers, &format!("{}/.ai-consumption-tracker/auth.json", home), "AI Consumption Tracker").await;
        check_config_file(providers, &format!("{}/.local/share/ai-consumption-tracker/auth.json", home), "AI Consumption Tracker").await;
        
        #[cfg(target_os = "windows")]
        {
            // OpenCode (Windows)
            check_config_file(providers, &format!("{}\\AppData\\Local\\opencode\\auth.json", home), "OpenCode").await;
            check_config_file(providers, &format!("{}\\AppData\\Roaming\\opencode\\auth.json", home), "OpenCode").await;
            check_config_file(providers, &format!("{}\\.opencode\\auth.json", home), "OpenCode").await;
            
            // AI Consumption Tracker (Windows)
            check_config_file(providers, &format!("{}\\.ai-consumption-tracker\\auth.json", home), "AI Consumption Tracker").await;
            check_config_file(providers, &format!("{}\\AppData\\Local\\ai-consumption-tracker\\auth.json", home), "AI Consumption Tracker").await;
            check_config_file(providers, &format!("{}\\AppData\\Roaming\\ai-consumption-tracker\\auth.json", home), "AI Consumption Tracker").await;
        }
    }
}

async fn check_config_file(providers: &mut Vec<ProviderConfig>, path: &str, source_name: &str) {
    debug!("Checking config file: {}", path);
    if let Ok(content) = tokio::fs::read_to_string(path).await {
        info!("Found config file: {} (source: {})", path, source_name);
        if let Ok(raw_configs) = serde_json::from_str::<serde_json::Value>(&content) {
            if let Some(obj) = raw_configs.as_object() {
                for (provider_id, value) in obj {
                    if provider_id != "app_settings" {
                        if let Some(api_key) = value.get("key").and_then(|v| v.as_str()) {
                            if !api_key.is_empty() {
                                add_or_update_provider(providers, provider_id, api_key, source_name);
                                info!("Loaded API key for {} from {} config file", provider_id, source_name);
                            }
                        }
                    }
                }
            }
        }
    }
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

impl Default for AgentConfig {
    fn default() -> Self {
        Self {
            refresh_interval_minutes: 5,
            auto_refresh_enabled: true,
            discovered_providers: Vec::new(),
        }
    }
}
