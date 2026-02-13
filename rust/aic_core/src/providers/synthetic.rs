use crate::models::{PaymentType, ProviderConfig, ProviderUsage};
use crate::provider::ProviderService;
use async_trait::async_trait;
use chrono::{DateTime, Utc};
use reqwest::Client;
use serde::Deserialize;

pub struct SyntheticProvider {
    client: Client,
}

impl SyntheticProvider {
    pub fn new(client: Client) -> Self {
        Self { client }
    }

    /// Try to load provider URL from providers.json file
    async fn get_url_from_providers_file(&self) -> Option<String> {
        let providers_paths = [
            directories::BaseDirs::new()
                .map(|base| base.home_dir().join(".local/share/opencode/providers.json"))
                .unwrap_or_default(),
            directories::BaseDirs::new()
                .map(|base| base.home_dir().join(".config/opencode/providers.json"))
                .unwrap_or_default(),
        ];

        for path in &providers_paths {
            if path.exists() {
                if let Ok(content) = tokio::fs::read_to_string(path).await {
                    if let Ok(providers) = serde_json::from_str::<std::collections::HashMap<String, String>>(&content) {
                        if let Some(url) = providers.get("synthetic") {
                            if !url.is_empty() {
                                return Some(url.clone());
                            }
                        }
                    }
                }
            }
        }

        None
    }
}

#[derive(Debug, Deserialize)]
struct SyntheticResponse {
    subscription: Option<SyntheticSubscription>,
}

#[derive(Debug, Deserialize)]
struct SyntheticSubscription {
    limit: f64,
    requests: f64,
    #[serde(rename = "renewsAt")]
    renews_at: Option<String>,
}

#[async_trait]
impl ProviderService for SyntheticProvider {
    fn provider_id(&self) -> &'static str {
        "synthetic"
    }

    async fn get_usage(&self, config: &ProviderConfig) -> Vec<ProviderUsage> {
        if config.api_key.is_empty() {
            return vec![ProviderUsage {
                provider_id: self.provider_id().to_string(),
                provider_name: "Synthetic".to_string(),
                is_available: false,
                description: "API Key not found".to_string(),
                ..Default::default()
            }];
        }

        // Get URL from config or try providers.json
        let url = match &config.base_url {
            Some(url) => url.clone(),
            None => {
                // Try to load URL from providers.json
                if let Some(url) = self.get_url_from_providers_file().await {
                    url
                } else {
                    return vec![ProviderUsage {
                        provider_id: self.provider_id().to_string(),
                        provider_name: "Synthetic".to_string(),
                        is_available: false,
                        description: "Configuration Required (Add 'base_url' to auth.json)".to_string(),
                        ..Default::default()
                    }];
                }
            }
        };

        match self
            .client
            .get(&url)
            .header("Authorization", &config.api_key)
            .send()
            .await
        {
            Ok(response) => {
                if !response.status().is_success() {
                    return vec![ProviderUsage {
                        provider_id: self.provider_id().to_string(),
                        provider_name: "Synthetic".to_string(),
                        is_available: false,
                        description: format!("API Error ({})", response.status()),
                        ..Default::default()
                    }];
                }

                match response.json::<SyntheticResponse>().await {
                    Ok(data) => {
                        if let Some(sub) = data.subscription {
                            let total = sub.limit;
                            let used = sub.requests;
                            
                            let utilization = if total > 0.0 {
                                (used / total) * 100.0
                            } else {
                                0.0
                            };
                            
                            let remaining_percent = 100.0 - utilization.min(100.0);
                            
                            let next_reset_time = sub.renews_at.and_then(|renews_at| {
                                DateTime::parse_from_rfc3339(&renews_at)
                                    .ok()
                                    .map(|dt| dt.with_timezone(&Utc))
                            });

                            vec![ProviderUsage {
                                provider_id: self.provider_id().to_string(),
                                provider_name: "Synthetic".to_string(),
                                usage_percentage: utilization.min(100.0),
                                remaining_percentage: Some(remaining_percent),
                                cost_used: used,
                                cost_limit: total,
                                payment_type: PaymentType::Quota,
                                usage_unit: "Quota %".to_string(),
                                is_quota_based: true,
                                description: format!("{:.1}% used", utilization),
                                next_reset_time,
                                ..Default::default()
                            }]
                        } else {
                            vec![ProviderUsage {
                                provider_id: self.provider_id().to_string(),
                                provider_name: "Synthetic".to_string(),
                                is_available: false,
                                description: "No subscription data found".to_string(),
                                ..Default::default()
                            }]
                        }
                    }
                    Err(_) => {
                        vec![ProviderUsage {
                            provider_id: self.provider_id().to_string(),
                            provider_name: "Synthetic".to_string(),
                            is_available: false,
                            description: "Failed to parse response".to_string(),
                            ..Default::default()
                        }]
                    }
                }
            }
            Err(_) => {
                vec![ProviderUsage {
                    provider_id: self.provider_id().to_string(),
                    provider_name: "Synthetic".to_string(),
                    is_available: false,
                    description: "Connection Failed".to_string(),
                    ..Default::default()
                }]
            }
        }
    }
}
