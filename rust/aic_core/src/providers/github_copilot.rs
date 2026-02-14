use crate::models::{PaymentType, ProviderConfig, ProviderUsage};
use crate::provider::ProviderService;
use async_trait::async_trait;
use chrono::{DateTime, Utc};
use log::warn;
use reqwest::Client;
use serde::Deserialize;
use serde_json::Value;
use std::collections::HashMap;

pub struct GitHubCopilotProvider {
    client: Client,
}

impl GitHubCopilotProvider {
    pub fn new(client: Client) -> Self {
        Self { client }
    }
}

#[derive(Debug, Deserialize)]
struct GitHubUserResponse {
    login: String,
}

#[derive(Debug, Deserialize)]
struct GitHubCopilotTokenResponse {
    sku: Option<String>,
    #[serde(rename = "expires_at")]
    #[allow(dead_code)]
    expires_at: Option<i64>,
    #[allow(dead_code)]
    limits: Option<Value>,
}

#[derive(Debug, Deserialize)]
struct GitHubRateLimitResource {
    limit: i32,
    remaining: i32,
    reset: i64,
}

#[derive(Debug, Deserialize)]
struct GitHubRateLimitResponse {
    resources: HashMap<String, GitHubRateLimitResource>,
}

#[async_trait]
impl ProviderService for GitHubCopilotProvider {
    fn provider_id(&self) -> &'static str {
        "github-copilot"
    }

    async fn get_usage(&self, config: &ProviderConfig) -> Vec<ProviderUsage> {
        let token = if !config.api_key.is_empty() {
            config.api_key.clone()
        } else {
            // Try to get token from config or environment
            std::env::var("GITHUB_TOKEN").unwrap_or_default()
        };

        if token.is_empty() {
            return vec![ProviderUsage {
                provider_id: self.provider_id().to_string(),
                provider_name: "GitHub Copilot".to_string(),
                is_available: false,
                description: "Not authenticated. Please login in Settings.".to_string(),
                is_quota_based: true,
                payment_type: PaymentType::Quota,
                ..Default::default()
            }];
        }

        let mut username = "User".to_string();
        let mut plan_name = "Unknown Plan".to_string();
        let mut reset_time: Option<DateTime<Utc>> = None;
        let mut percentage = 0.0;
        let mut cost_used = 0.0;
        let mut cost_limit = 0.0;
        
        let mut raw_user: Option<String> = None;
        let mut raw_token: Option<String> = None;
        let mut raw_rate_limit: Option<String> = None;

        // Fetch user info
        match self
            .client
            .get("https://api.github.com/user")
            .header("Authorization", format!("Bearer {}", token))
            .header("User-Agent", "AIConsumptionTracker/1.0")
            .send()
            .await
        {
            Ok(response) => {
                if response.status().is_success() {
                    let raw = response.text().await.unwrap_or_default();
                    raw_user = Some(raw.clone());
                    if let Ok(user_data) = serde_json::from_str::<GitHubUserResponse>(&raw) {
                        username = user_data.login;
                    }
                }
            }
            Err(e) => {
                warn!("Failed to fetch GitHub user: {}", e);
            }
        }

        // Fetch Copilot token info
        match self
            .client
            .get("https://api.github.com/copilot_internal/v2/token")
            .header("Authorization", format!("Bearer {}", token))
            .header("User-Agent", "AIConsumptionTracker/1.0")
            .send()
            .await
        {
            Ok(response) => {
                if response.status().is_success() {
                    let raw = response.text().await.unwrap_or_default();
                    raw_token = Some(raw.clone());
                    if let Ok(token_data) = serde_json::from_str::<GitHubCopilotTokenResponse>(&raw) {
                        plan_name = match token_data.sku.as_deref() {
                            Some("copilot_individual") => "Copilot Individual".to_string(),
                            Some("copilot_business") => "Copilot Business".to_string(),
                            Some("copilot_enterprise") => "Copilot Enterprise".to_string(),
                            Some(sku) => sku.to_string(),
                            None => "Unknown Plan".to_string(),
                        };
                    }
                }
            }
            Err(e) => {
                warn!("Failed to fetch Copilot token: {}", e);
            }
        }

        // Fetch rate limits
        match self
            .client
            .get("https://api.github.com/rate_limit")
            .header("Authorization", format!("Bearer {}", token))
            .header("User-Agent", "AIConsumptionTracker/1.0")
            .send()
            .await
        {
            Ok(response) => {
                if response.status().is_success() {
                    let raw = response.text().await.unwrap_or_default();
                    raw_rate_limit = Some(raw.clone());
                    if let Ok(rate_data) = serde_json::from_str::<GitHubRateLimitResponse>(&raw) {
                        if let Some(core) = rate_data.resources.get("core") {
                            cost_limit = core.limit as f64;
                            cost_used = (core.limit - core.remaining) as f64;
                            percentage = if core.limit > 0 {
                                ((core.limit - core.remaining) as f64 / core.limit as f64) * 100.0
                            } else {
                                0.0
                            };
                            reset_time = Some(
                                DateTime::from_timestamp(core.reset, 0)
                                    .unwrap_or_else(|| Utc::now()),
                            );
                        }
                    }
                }
            }
            Err(e) => {
                warn!("Failed to fetch rate limit: {}", e);
            }
        }

        // Combine raw responses into a single JSON object
        let raw_response = serde_json::json!({
            "user": raw_user,
            "copilot_token": raw_token,
            "rate_limit": raw_rate_limit
        }).to_string();

        vec![ProviderUsage {
            provider_id: self.provider_id().to_string(),
            provider_name: "GitHub Copilot".to_string(),
            account_name: username,
            is_available: true,
            usage_percentage: percentage,
            cost_used,
            cost_limit,
            usage_unit: "Reqs".to_string(),
            payment_type: PaymentType::Quota,
            is_quota_based: true,
            description: format!(
                "API Rate Limit (Hourly): {:.0}/{:.0} Used",
                cost_used, cost_limit
            ),
            auth_source: plan_name,
            next_reset_time: reset_time,
            raw_response: Some(raw_response),
            ..Default::default()
        }]
    }
}
