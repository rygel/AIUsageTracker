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
    
    // Add well-known providers
    let well_known = vec![
        "openai",
        "minimax",
        "xiaomi",
        "kimi",
        "kilocode",
        "claude-code",
        "gemini-cli",
        "antigravity",
        "deepseek",
        "openrouter",
        "zai",
    ];
    
    for id in well_known {
        providers.push(ProviderConfig {
            provider_id: id.to_string(),
            api_key: String::new(),
            config_type: "pay-as-you-go".to_string(),
            description: Some("Well-known provider".to_string()),
            auth_source: "System Default".to_string(),
            ..Default::default()
        });
    }
    
    // Discover from environment variables
    if let Ok(openai_key) = std::env::var("OPENAI_API_KEY") {
        if !openai_key.is_empty() {
            add_or_update_provider(&mut providers, "openai", &openai_key, "Environment Variable");
        }
    }
    
    if let Ok(anthropic_key) = std::env::var("ANTHROPIC_API_KEY").or_else(|_| std::env::var("CLAUDE_API_KEY")) {
        if !anthropic_key.is_empty() {
            add_or_update_provider(&mut providers, "claude-code", &anthropic_key, "Environment Variable");
        }
    }
    
    if let Ok(gemini_key) = std::env::var("GEMINI_API_KEY").or_else(|_| std::env::var("GOOGLE_API_KEY")) {
        if !gemini_key.is_empty() {
            add_or_update_provider(&mut providers, "gemini-cli", &gemini_key, "Environment Variable");
        }
    }
    
    if let Ok(deepseek_key) = std::env::var("DEEPSEEK_API_KEY") {
        if !deepseek_key.is_empty() {
            add_or_update_provider(&mut providers, "deepseek", &deepseek_key, "Environment Variable");
        }
    }
    
    if let Ok(kimi_key) = std::env::var("KIMI_API_KEY").or_else(|_| std::env::var("MOONSHOT_API_KEY")) {
        if !kimi_key.is_empty() {
            add_or_update_provider(&mut providers, "kimi", &kimi_key, "Environment Variable");
        }
    }
    
    // Discover from existing config files (for migration)
    if let Ok(home) = std::env::var("HOME") {
        let config_paths = vec![
            format!("{}/.ai-consumption-tracker/auth.json", home),
            format!("{}/.local/share/opencode/auth.json", home),
        ];
        
        for path in config_paths {
            if let Ok(content) = tokio::fs::read_to_string(&path).await {
                if let Ok(raw_configs) = serde_json::from_str::<serde_json::Value>(&content) {
                    if let Some(obj) = raw_configs.as_object() {
                        for (provider_id, value) in obj {
                            if provider_id != "app_settings" {
                                if let Some(api_key) = value.get("key").and_then(|v| v.as_str()) {
                                    if !api_key.is_empty() {
                                        add_or_update_provider(&mut providers, provider_id, api_key, "Config File");
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
    
    info!("Discovered {} providers", providers.len());
    providers
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
