use crate::models::{AppPreferences, ProviderConfig, ProviderUsage};
use crate::provider::ProviderService;
use crate::providers::*;
use log::{debug, warn};
use reqwest::Client;
use std::collections::HashSet;
use std::path::PathBuf;
use std::sync::Arc;
use tokio::sync::{Mutex, Semaphore};

pub struct ConfigLoader {
    client: Client,
    custom_path: Option<PathBuf>,
}

impl ConfigLoader {
    pub fn new(client: Client) -> Self {
        Self {
            client,
            custom_path: None,
        }
    }

    /// Create a ConfigLoader with a custom config path (useful for testing)
    pub fn with_custom_path(client: Client, path: PathBuf) -> Self {
        Self {
            client,
            custom_path: Some(path),
        }
    }

    fn get_tracker_config_path(&self) -> PathBuf {
        if let Some(ref custom) = self.custom_path {
            let path = custom.join("auth.json");
            log::info!("Using custom config path: {:?}", path);
            return path;
        }
        let path = directories::BaseDirs::new()
            .map(|base| {
                let p = base.home_dir()
                    .join(".ai-consumption-tracker")
                    .join("auth.json");
                log::info!("Using config path: {:?}", p);
                p
            })
            .unwrap_or_else(|| {
                let fallback = PathBuf::from(".ai-consumption-tracker/auth.json");
                log::warn!("Using fallback config path: {:?}", fallback);
                fallback
            });
        path
    }

    pub async fn load_config(&self) -> Vec<ProviderConfig> {
        // If custom path is set (for testing), only use that path
        let paths: Vec<PathBuf> = if self.custom_path.is_some() {
            vec![self.get_tracker_config_path()]
        } else {
            vec![
                self.get_tracker_config_path(),
                directories::BaseDirs::new()
                    .map(|base| base.home_dir().join(".local/share/opencode/auth.json"))
                    .unwrap_or_default(),
                directories::BaseDirs::new()
                    .map(|base| base.data_dir().join("opencode/auth.json"))
                    .unwrap_or_default(),
                directories::BaseDirs::new()
                    .map(|base| base.data_local_dir().join("opencode/auth.json"))
                    .unwrap_or_default(),
                directories::BaseDirs::new()
                    .map(|base| base.home_dir().join(".opencode/auth.json"))
                    .unwrap_or_default(),
            ]
        };

        let mut result = Vec::new();
        let mut processed_providers: HashSet<String> = HashSet::new();

        for path in paths {
            if path.exists() {
                if let Ok(content) = tokio::fs::read_to_string(&path).await {
                    if let Ok(raw_configs) =
                        serde_json::from_str::<serde_json::Map<String, serde_json::Value>>(&content)
                    {
                        for (provider_id, value) in raw_configs {
                            // Skip app_settings
                            if provider_id.eq_ignore_ascii_case("app_settings") {
                                continue;
                            }

                            let normalized_id =
                                if provider_id.eq_ignore_ascii_case("kimi-for-coding") {
                                    "kimi".to_string()
                                } else {
                                    provider_id.clone()
                                };

                            if processed_providers.contains(&normalized_id) {
                                continue;
                            }

                            if let Some(obj) = value.as_object() {
                                let api_key = obj
                                    .get("key")
                                    .and_then(|v| v.as_str())
                                    .unwrap_or("")
                                    .to_string();
                                let config_type = obj
                                    .get("type")
                                    .and_then(|v| v.as_str())
                                    .unwrap_or("api")
                                    .to_string();
                                let base_url = obj
                                    .get("base_url")
                                    .and_then(|v| v.as_str())
                                    .map(|s| s.to_string());
                                let show_in_tray = obj
                                    .get("show_in_tray")
                                    .and_then(|v| v.as_bool())
                                    .unwrap_or(false);
                                let enabled_sub_trays = obj
                                    .get("enabled_sub_trays")
                                    .and_then(|v| v.as_array())
                                    .map(|arr| {
                                        arr.iter()
                                            .filter_map(|v| v.as_str().map(|s| s.to_string()))
                                            .collect()
                                    })
                                    .unwrap_or_default();

                                result.push(ProviderConfig {
                                    provider_id: normalized_id.clone(),
                                    api_key,
                                    config_type,
                                    limit: Some(100.0),
                                    base_url,
                                    show_in_tray,
                                    enabled_sub_trays,
                                    auth_source: format!(
                                        "Config: {}",
                                        path.file_name().unwrap_or_default().to_string_lossy()
                                    ),
                                    description: None,
                                    ..Default::default()
                                });
                                processed_providers.insert(normalized_id);
                            }
                        }
                    }
                }
            }
        }

        // Add discovered tokens
        let discovered = self.discover_tokens().await;
        for d in discovered {
            if !result
                .iter()
                .any(|r| r.provider_id.eq_ignore_ascii_case(&d.provider_id))
            {
                result.push(d);
            } else if let Some(existing) = result
                .iter_mut()
                .find(|r| r.provider_id.eq_ignore_ascii_case(&d.provider_id))
            {
                if existing.api_key.is_empty() && !d.api_key.is_empty() {
                    existing.api_key = d.api_key;
                    existing.description = d.description;
                    if existing.base_url.is_none() {
                        existing.base_url = d.base_url;
                    }
                }
            }
        }

        result
    }

    /// Load only primary config file (fast, no discovery)
    pub async fn load_primary_config(&self) -> Vec<ProviderConfig> {
        let path = self.get_tracker_config_path();
        let mut result = Vec::new();

        if path.exists() {
            if let Ok(content) = tokio::fs::read_to_string(&path).await {
                if let Ok(raw_configs) =
                    serde_json::from_str::<serde_json::Map<String, serde_json::Value>>(&content)
                {
                    for (provider_id, value) in raw_configs {
                        // Skip app_settings
                        if provider_id.eq_ignore_ascii_case("app_settings") {
                            continue;
                        }

                        let normalized_id =
                            if provider_id.eq_ignore_ascii_case("kimi-for-coding") {
                                "kimi".to_string()
                            } else {
                                provider_id.clone()
                            };

                        if let Some(obj) = value.as_object() {
                            let api_key = obj
                                .get("key")
                                .and_then(|v| v.as_str())
                                .unwrap_or("")
                                .to_string();
                            let config_type = obj
                                .get("type")
                                .and_then(|v| v.as_str())
                                .unwrap_or("api")
                                .to_string();
                            let base_url = obj
                                .get("base_url")
                                .and_then(|v| v.as_str())
                                .map(|s| s.to_string());
                            let show_in_tray = obj
                                .get("show_in_tray")
                                .and_then(|v| v.as_bool())
                                .unwrap_or(false);
                            let enabled_sub_trays = obj
                                .get("enabled_sub_trays")
                                .and_then(|v| v.as_array())
                                .map(|arr| {
                                    arr.iter()
                                        .filter_map(|v| v.as_str().map(|s| s.to_string()))
                                        .collect()
                                })
                                .unwrap_or_default();

                            result.push(ProviderConfig {
                                provider_id: normalized_id.clone(),
                                api_key,
                                config_type,
                                limit: Some(100.0),
                                base_url,
                                show_in_tray,
                                enabled_sub_trays,
                                auth_source: format!(
                                    "Config: {}",
                                    path.file_name().unwrap_or_default().to_string_lossy()
                                ),
                                description: None,
                                ..Default::default()
                            });
                        }
                    }
                }
            }
        }

        result
    }

    async fn discover_tokens(&self) -> Vec<ProviderConfig> {
        let mut discovered = Vec::new();

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
        ];
        for id in well_known {
            discovered.push(ProviderConfig {
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
                Self::add_or_update(
                    &mut discovered,
                    "openai",
                    &openai_key,
                    "Discovered via Environment Variable",
                    "Env: OPENAI_API_KEY",
                );
            }
        }

        if let Ok(anthropic_key) =
            std::env::var("ANTHROPIC_API_KEY").or_else(|_| std::env::var("CLAUDE_API_KEY"))
        {
            if !anthropic_key.is_empty() {
                Self::add_or_update(
                    &mut discovered,
                    "claude-code",
                    &anthropic_key,
                    "Discovered via Environment Variable",
                    "Env: ANTHROPIC_API_KEY",
                );
            }
        }

        if let Ok(gemini_key) =
            std::env::var("GEMINI_API_KEY").or_else(|_| std::env::var("GOOGLE_API_KEY"))
        {
            if !gemini_key.is_empty() {
                Self::add_or_update(
                    &mut discovered,
                    "gemini-cli",
                    &gemini_key,
                    "Discovered via Environment Variable",
                    "Env: GEMINI_API_KEY",
                );
            }
        }

        if let Ok(deepseek_key) = std::env::var("DEEPSEEK_API_KEY") {
            if !deepseek_key.is_empty() {
                Self::add_or_update(
                    &mut discovered,
                    "deepseek",
                    &deepseek_key,
                    "Discovered via Environment Variable",
                    "Env: DEEPSEEK_API_KEY",
                );
            }
        }

        if let Ok(openrouter_key) = std::env::var("OPENROUTER_API_KEY") {
            if !openrouter_key.is_empty() {
                Self::add_or_update(
                    &mut discovered,
                    "openrouter",
                    &openrouter_key,
                    "Discovered via Environment Variable",
                    "Env: OPENROUTER_API_KEY",
                );
            }
        }

        if let Ok(kimi_key) =
            std::env::var("KIMI_API_KEY").or_else(|_| std::env::var("MOONSHOT_API_KEY"))
        {
            if !kimi_key.is_empty() {
                Self::add_or_update(
                    &mut discovered,
                    "kimi",
                    &kimi_key,
                    "Discovered via Environment Variable",
                    "Env: KIMI_API_KEY",
                );
            }
        }

        if let Ok(xiaomi_key) =
            std::env::var("XIAOMI_API_KEY").or_else(|_| std::env::var("MIMO_API_KEY"))
        {
            if !xiaomi_key.is_empty() {
                Self::add_or_update(
                    &mut discovered,
                    "xiaomi",
                    &xiaomi_key,
                    "Discovered via Environment Variable",
                    "Env: XIAOMI_API_KEY",
                );
            }
        }

        if let Ok(minimax_key) = std::env::var("MINIMAX_API_KEY") {
            if !minimax_key.is_empty() {
                Self::add_or_update(
                    &mut discovered,
                    "minimax",
                    &minimax_key,
                    "Discovered via Environment Variable",
                    "Env: MINIMAX_API_KEY",
                );
            }
        }

        if let Ok(zai_key) = std::env::var("ZAI_API_KEY").or_else(|_| std::env::var("Z_AI_API_KEY"))
        {
            if !zai_key.is_empty() {
                Self::add_or_update(
                    &mut discovered,
                    "zai",
                    &zai_key,
                    "Discovered via Environment Variable",
                    "Env: ZAI_API_KEY",
                );
            }
        }

        if let Ok(antigravity_key) = std::env::var("ANTIGRAVITY_API_KEY")
            .or_else(|_| std::env::var("GOOGLE_ANTIGRAVITY_API_KEY"))
        {
            if !antigravity_key.is_empty() {
                Self::add_or_update(
                    &mut discovered,
                    "antigravity",
                    &antigravity_key,
                    "Discovered via Environment Variable",
                    "Env: ANTIGRAVITY_API_KEY",
                );
            }
        }

        if let Ok(opencode_key) = std::env::var("OPENCODE_API_KEY") {
            if !opencode_key.is_empty() {
                Self::add_or_update(
                    &mut discovered,
            "opencode-zen",
                    &opencode_key,
                    "Discovered via Environment Variable",
                    "Env: OPENCODE_API_KEY",
                );
            }
        }

        if let Ok(cloudcode_key) = std::env::var("CLOUDCODE_API_KEY") {
            if !cloudcode_key.is_empty() {
                Self::add_or_update(
                    &mut discovered,
                    "cloudcode",
                    &cloudcode_key,
                    "Discovered via Environment Variable",
                    "Env: CLOUDCODE_API_KEY",
                );
            }
        }

        if let Ok(codex_key) = std::env::var("CODEX_API_KEY") {
            if !codex_key.is_empty() {
                Self::add_or_update(
                    &mut discovered,
                    "codex",
                    &codex_key,
                    "Discovered via Environment Variable",
                    "Env: CODEX_API_KEY",
                );
            }
        }

        // Discover from Kilo Code
        Self::discover_kilo_code_tokens(&mut discovered).await;

        // Discover from providers.json
        Self::discover_from_providers_file(&mut discovered).await;

        discovered
    }

    fn add_or_update(
        configs: &mut Vec<ProviderConfig>,
        provider_id: &str,
        key: &str,
        description: &str,
        source: &str,
    ) {
        if let Some(existing) = configs
            .iter_mut()
            .find(|c| c.provider_id.eq_ignore_ascii_case(provider_id))
        {
            if !key.is_empty() {
                existing.api_key = key.to_string();
                existing.description = Some(description.to_string());
                existing.auth_source = source.to_string();
            }
        } else {
            configs.push(ProviderConfig {
                provider_id: provider_id.to_string(),
                api_key: key.to_string(),
                config_type: "pay-as-you-go".to_string(),
                description: Some(description.to_string()),
                auth_source: source.to_string(),
                ..Default::default()
            });
        }
    }

    async fn discover_kilo_code_tokens(configs: &mut Vec<ProviderConfig>) {
        let kilo_path = directories::BaseDirs::new()
            .map(|base| base.home_dir().join(".kilocode/secrets.json"))
            .unwrap_or_default();

        if kilo_path.exists() {
            if let Ok(content) = tokio::fs::read_to_string(&kilo_path).await {
                if let Ok(json) = serde_json::from_str::<serde_json::Value>(&content) {
                    if let Some(kilo_entry) = json.get("kilo code.kilo-code") {
                        // Direct kilocodeToken
                        if let Some(token) =
                            kilo_entry.get("kilocodeToken").and_then(|v| v.as_str())
                        {
                            if !token.is_empty() {
                                Self::add_or_update(
                                    configs,
                                    "kilocode",
                                    token,
                                    "Discovered in Kilo Code secrets",
                                    "Kilo Code Secrets",
                                );
                            }
                        }

                        // Nested Roo Cline config
                        if let Some(roo_json) = kilo_entry
                            .get("roo_cline_config_api_config")
                            .and_then(|v| v.as_str())
                        {
                            Self::parse_roo_config(configs, roo_json).await;
                        }
                    }
                }
            }
        }
    }

    async fn parse_roo_config(configs: &mut Vec<ProviderConfig>, roo_json: &str) {
        if let Ok(roo_doc) = serde_json::from_str::<serde_json::Value>(roo_json) {
            if let Some(api_configs) = roo_doc.get("apiConfigs") {
                if let Some(obj) = api_configs.as_object() {
                    for (_, config) in obj {
                        Self::try_add_roo_key(configs, config, "anthropicApiKey", "anthropic");
                        Self::try_add_roo_key(configs, config, "openAiApiKey", "openai");
                        Self::try_add_roo_key(configs, config, "geminiApiKey", "gemini");
                        Self::try_add_roo_key(configs, config, "openrouterApiKey", "openrouter");
                        Self::try_add_roo_key(configs, config, "mistralApiKey", "mistral");
                        Self::try_add_roo_key(configs, config, "kilocodeToken", "kilocode");
                    }
                }
            }
        }
    }

    fn try_add_roo_key(
        configs: &mut Vec<ProviderConfig>,
        config: &serde_json::Value,
        prop_name: &str,
        provider_id: &str,
    ) {
        if let Some(key) = config.get(prop_name).and_then(|v| v.as_str()) {
            if !key.is_empty() {
                Self::add_or_update(
                    configs,
                    provider_id,
                    key,
                    "Discovered in Kilo Code (Roo Config)",
                    "Kilo Code Roo Config",
                );
            }
        }
    }

    async fn discover_from_providers_file(configs: &mut Vec<ProviderConfig>) {
        let providers_path = directories::BaseDirs::new()
            .map(|base| base.home_dir().join(".local/share/opencode/providers.json"))
            .unwrap_or_default();

        if providers_path.exists() {
            if let Ok(content) = tokio::fs::read_to_string(&providers_path).await {
                if let Ok(known) = serde_json::from_str::<serde_json::Value>(&content) {
                    if let Some(obj) = known.as_object() {
                        for (id, _) in obj {
                            Self::add_or_update(
                                configs,
                                id,
                                "",
                                "Discovered in providers.json",
                                "Config: providers.json",
                            );
                        }
                    }
                }
            }
        }
    }

    pub async fn save_config(
        &self,
        configs: &[ProviderConfig],
    ) -> Result<(), Box<dyn std::error::Error>> {
        let path = self.get_tracker_config_path();
        if let Some(parent) = path.parent() {
            tokio::fs::create_dir_all(parent).await?;
        }

        let mut export = serde_json::Map::new();
        for config in configs {
            if config.api_key.is_empty() && config.base_url.is_none() {
                continue;
            }

            let mut entry = serde_json::Map::new();
            entry.insert(
                "key".to_string(),
                serde_json::Value::String(config.api_key.clone()),
            );
            entry.insert(
                "type".to_string(),
                serde_json::Value::String(config.config_type.clone()),
            );
            entry.insert(
                "show_in_tray".to_string(),
                serde_json::Value::Bool(config.show_in_tray),
            );
            entry.insert(
                "enabled_sub_trays".to_string(),
                serde_json::Value::Array(
                    config
                        .enabled_sub_trays
                        .iter()
                        .map(|s| serde_json::Value::String(s.clone()))
                        .collect(),
                ),
            );
            if let Some(ref base_url) = config.base_url {
                entry.insert(
                    "base_url".to_string(),
                    serde_json::Value::String(base_url.clone()),
                );
            }

            export.insert(config.provider_id.clone(), serde_json::Value::Object(entry));
        }

        // Preserve app_settings if it exists
        if path.exists() {
            if let Ok(content) = tokio::fs::read_to_string(&path).await {
                if let Ok(existing) =
                    serde_json::from_str::<serde_json::Map<String, serde_json::Value>>(&content)
                {
                    if let Some(settings) = existing.get("app_settings") {
                        export.insert("app_settings".to_string(), settings.clone());
                    }
                }
            }
        }

        let json = serde_json::to_string_pretty(&export)?;
        tokio::fs::write(path, json).await?;
        Ok(())
    }

    pub async fn load_preferences(&self) -> AppPreferences {
        // Try loading from auth.json first
        let auth_path = self.get_tracker_config_path();
        if auth_path.exists() {
            if let Ok(content) = tokio::fs::read_to_string(&auth_path).await {
                if let Ok(root) =
                    serde_json::from_str::<serde_json::Map<String, serde_json::Value>>(&content)
                {
                    if let Some(settings) = root.get("app_settings") {
                        if let Ok(prefs) =
                            serde_json::from_value::<AppPreferences>(settings.clone())
                        {
                            return prefs;
                        }
                    }
                }
            }
        }

        // Fallback to old preferences.json (only if not using custom path)
        if self.custom_path.is_none() {
            let prefs_path = directories::BaseDirs::new()
                .map(|base| {
                    base.home_dir()
                        .join(".ai-consumption-tracker/preferences.json")
                })
                .unwrap_or_default();

            if prefs_path.exists() {
                if let Ok(content) = tokio::fs::read_to_string(&prefs_path).await {
                    if let Ok(prefs) = serde_json::from_str::<AppPreferences>(&content) {
                        return prefs;
                    }
                }
            }
        }

        AppPreferences::default()
    }

    pub async fn save_preferences(
        &self,
        preferences: &AppPreferences,
    ) -> Result<(), Box<dyn std::error::Error>> {
        let path = self.get_tracker_config_path();
        if let Some(parent) = path.parent() {
            tokio::fs::create_dir_all(parent).await?;
        }

        let mut root: serde_json::Map<String, serde_json::Value> = if path.exists() {
            if let Ok(content) = tokio::fs::read_to_string(&path).await {
                serde_json::from_str(&content).unwrap_or_default()
            } else {
                serde_json::Map::new()
            }
        } else {
            serde_json::Map::new()
        };

        root.insert(
            "app_settings".to_string(),
            serde_json::to_value(preferences)?,
        );

        let json = serde_json::to_string_pretty(&root)?;
        tokio::fs::write(path, json).await?;
        Ok(())
    }
}

pub struct ProviderManager {
    providers: Vec<Arc<dyn ProviderService>>,
    config_loader: Arc<ConfigLoader>,
    last_usages: Arc<Mutex<Vec<ProviderUsage>>>,
    refresh_semaphore: Arc<Semaphore>,
}

impl ProviderManager {
    pub fn new(client: Client) -> Self {
        let config_loader = Arc::new(ConfigLoader::new(client.clone()));

        // Register all providers
        let mut providers: Vec<Arc<dyn ProviderService>> = Vec::new();
        providers.push(Arc::new(OpenAIProvider::new(client.clone())));
        providers.push(Arc::new(AnthropicProvider));
        providers.push(Arc::new(DeepSeekProvider::new(client.clone())));
        providers.push(Arc::new(SimulatedProvider));
        providers.push(Arc::new(OpenRouterProvider::new(client.clone())));
        providers.push(Arc::new(OpenCodeProvider::new(client.clone())));
        providers.push(Arc::new(OpenCodeZenProvider::new()));
        providers.push(Arc::new(CodexProvider));
        providers.push(Arc::new(GitHubCopilotProvider::new(client.clone())));
        providers.push(Arc::new(AntigravityProvider::new()));
        providers.push(Arc::new(KimiProvider::new(client.clone())));
        providers.push(Arc::new(MinimaxProvider::new(client.clone())));
        providers.push(Arc::new(MinimaxIOProvider::new(client.clone())));
        providers.push(Arc::new(ZaiProvider::new(client.clone())));
        providers.push(Arc::new(SyntheticProvider::new(client.clone())));
        providers.push(Arc::new(MistralProvider::new(client.clone())));
        providers.push(Arc::new(GenericPayAsYouGoProvider::new(client.clone())));
        providers.push(Arc::new(GeminiProvider::new(client.clone())));

        Self {
            providers,
            config_loader,
            last_usages: Arc::new(Mutex::new(Vec::new())),
            refresh_semaphore: Arc::new(Semaphore::new(1)),
        }
    }

    pub async fn get_all_usage(&self, force_refresh: bool) -> Vec<ProviderUsage> {
        let _permit = self.refresh_semaphore.acquire().await.unwrap();

        if !force_refresh {
            let usages: tokio::sync::MutexGuard<'_, Vec<ProviderUsage>> =
                self.last_usages.lock().await;
            if !usages.is_empty() {
                return usages.clone();
            }
        }

        let usages: Vec<ProviderUsage> = self.fetch_all_usage().await;
        *self.last_usages.lock().await = usages.clone();
        usages
    }

    async fn fetch_all_usage(&self) -> Vec<ProviderUsage> {
        debug!("Starting fetch_all_usage...");
        let mut configs = self.config_loader.load_primary_config().await;

        // Auto-add system providers
        let system_providers = vec![
            "antigravity",
            "gemini-cli",
                    "opencode-zen",
            "github-copilot",
        ];
        for provider_id in system_providers {
            if !configs
                .iter()
                .any(|c| c.provider_id.eq_ignore_ascii_case(provider_id))
            {
                configs.push(ProviderConfig {
                    provider_id: provider_id.to_string(),
                    api_key: String::new(),
                    auth_source: "System".to_string(),
                    ..Default::default()
                });
            }
        }

        let mut tasks = Vec::new();
        for config in configs {
            let providers = self.providers.clone();
            let task = tokio::spawn(async move {
                let provider = providers.iter().find(|p| {
                    p.provider_id().eq_ignore_ascii_case(&config.provider_id)
                        || (p.provider_id() == "anthropic" && config.provider_id.contains("claude"))
                });

                let provider = provider.or_else(|| {
                    if config.config_type == "pay-as-you-go" || config.config_type == "api" {
                        providers
                            .iter()
                            .find(|p| p.provider_id() == "generic-pay-as-you-go")
                    } else {
                        None
                    }
                });

                if let Some(provider) = provider {
                    debug!("Fetching usage for provider: {}", config.provider_id);
                    let mut usages = provider.get_usage(&config).await;
                    for usage in &mut usages {
                        usage.auth_source = config.auth_source.clone();
                    }
                    debug!("Success for {}: {} items", config.provider_id, usages.len());
                    usages
                } else {
                    // Generic fallback
                    let display_name = config
                        .provider_id
                        .replace("-", " ")
                        .split_whitespace()
                        .map(|word| {
                            let mut chars = word.chars();
                            match chars.next() {
                                None => String::new(),
                                Some(first) => {
                                    first.to_uppercase().collect::<String>()
                                        + &chars.as_str().to_lowercase()
                                }
                            }
                        })
                        .collect::<Vec<_>>()
                        .join(" ");

                    vec![ProviderUsage {
                        provider_id: config.provider_id.clone(),
                        provider_name: display_name,
                        description: "Connected (Generic)".to_string(),
                        usage_unit: "USD".to_string(),
                        is_quota_based: false,
                        is_available: true,
                        ..Default::default()
                    }]
                }
            });
            tasks.push(task);
        }

        let mut results = Vec::new();
        for task in tasks {
            match task.await {
                Ok(usages) => results.extend(usages),
                Err(e) => {
                    log::error!("Task failed: {}", e);
                }
            }
        }

        results
    }

    pub async fn get_last_usages(&self) -> Vec<ProviderUsage> {
        let usages: tokio::sync::MutexGuard<'_, Vec<ProviderUsage>> = self.last_usages.lock().await;
        usages.clone()
    }
}
