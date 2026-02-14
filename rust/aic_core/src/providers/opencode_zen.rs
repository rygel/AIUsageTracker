use crate::models::{PaymentType, ProviderConfig, ProviderUsage};
use crate::provider::ProviderService;
use async_trait::async_trait;
use log::error;
use regex::Regex;
use std::path::PathBuf;
use std::process::{Command, Stdio};

pub struct OpenCodeZenProvider;

impl OpenCodeZenProvider {
    pub fn new() -> Self {
        Self
    }

    fn get_cli_path() -> Option<PathBuf> {
        if let Ok(home) = std::env::var("HOME") {
            let path = PathBuf::from(home).join(".npm").join("opencode.cmd");
            if path.exists() {
                return Some(path);
            }
        }

        if let Ok(home) = std::env::var("USERPROFILE") {
            let path = PathBuf::from(home)
                .join("AppData")
                .join("Roaming")
                .join("npm")
                .join("opencode.cmd");
            if path.exists() {
                return Some(path);
            }
        }

        None
    }
}

#[async_trait]
impl ProviderService for OpenCodeZenProvider {
    fn provider_id(&self) -> &'static str {
        "opencode-zen"
    }

    async fn get_usage(&self, _config: &ProviderConfig) -> Vec<ProviderUsage> {
        let cli_path = match Self::get_cli_path() {
            Some(path) => path,
            None => {
                return vec![ProviderUsage {
                    provider_id: self.provider_id().to_string(),
                    provider_name: "OpenCode Zen".to_string(),
                    is_available: false,
                    description: "CLI not found at expected path".to_string(),
                    ..Default::default()
                }];
            }
        };

        let output = match Command::new(&cli_path)
            .args(&["stats", "--days", "7", "--models", "10"])
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .output()
        {
            Ok(output) => output,
            Err(e) => {
                error!("Failed to run OpenCode CLI: {}", e);
                return vec![ProviderUsage {
                    provider_id: self.provider_id().to_string(),
                    provider_name: "OpenCode Zen".to_string(),
                    is_available: false,
                    description: format!("Execution Failed: {}", e),
                    ..Default::default()
                }];
            }
        };

        if !output.status.success() {
            let error_msg = String::from_utf8_lossy(&output.stderr);
            error!("OpenCode CLI failed: {}", error_msg);
            return vec![ProviderUsage {
                provider_id: self.provider_id().to_string(),
                provider_name: "OpenCode Zen".to_string(),
                is_available: false,
                description: format!("CLI Error: {}", output.status),
                ..Default::default()
            }];
        }

        let stdout = String::from_utf8_lossy(&output.stdout);

        let total_cost = Self::parse_total_cost(&stdout);
        let avg_cost = Self::parse_avg_cost(&stdout);

        let description = if total_cost > 0.0 {
            format!("${:.2} (7 days)", total_cost)
        } else {
            "No usage data".to_string()
        };

        vec![ProviderUsage {
            provider_id: self.provider_id().to_string(),
            provider_name: "OpenCode Zen".to_string(),
            usage_percentage: 0.0,
            cost_used: total_cost,
            cost_limit: 0.0,
            usage_unit: "USD".to_string(),
            is_quota_based: false,
            payment_type: PaymentType::UsageBased,
            is_available: true,
            description,
            ..Default::default()
        }]
    }
}

impl OpenCodeZenProvider {
    fn parse_total_cost(output: &str) -> f64 {
        let re = Regex::new(r"Total Cost\s+\$([0-9.]+)").unwrap();
        if let Some(captures) = re.captures(output) {
            if let Some(m) = captures.get(1) {
                return m.as_str().parse().unwrap_or(0.0);
            }
        }
        0.0
    }

    fn parse_avg_cost(output: &str) -> f64 {
        let re = Regex::new(r"Avg Cost/Day\s+\$([0-9.]+)").unwrap();
        if let Some(captures) = re.captures(output) {
            if let Some(m) = captures.get(1) {
                return m.as_str().parse().unwrap_or(0.0);
            }
        }
        0.0
    }
}