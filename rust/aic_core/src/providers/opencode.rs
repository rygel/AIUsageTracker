use crate::models::{PaymentType, ProviderConfig, ProviderUsage};
use crate::provider::ProviderService;
use async_trait::async_trait;
use log::error;
use reqwest::Client;
use serde::Deserialize;

pub struct OpenCodeProvider {
    client: Client,
}

impl OpenCodeProvider {
    pub fn new(client: Client) -> Self {
        Self { client }
    }
}

#[derive(Debug, Deserialize)]
struct OpenCodeCreditsResponse {
    data: Option<OpenCodeCreditsData>,
}

#[derive(Debug, Deserialize)]
struct OpenCodeCreditsData {
    #[serde(rename = "total_credits")]
    total_credits: f64,
    #[serde(rename = "used_credits")]
    used_credits: f64,
    #[serde(rename = "remaining_credits")]
    remaining_credits: f64,
}

#[async_trait]
impl ProviderService for OpenCodeProvider {
    fn provider_id(&self) -> &'static str {
        "opencode"
    }

    async fn get_usage(&self, config: &ProviderConfig) -> Vec<ProviderUsage> {
        if config.api_key.is_empty() {
            return vec![ProviderUsage {
                provider_id: self.provider_id().to_string(),
                provider_name: "OpenCode".to_string(),
                is_available: false,
                description: "API Key not found".to_string(),
                ..Default::default()
            }];
        }

        let url = "https://api.opencode.ai/v1/credits";

        match self
            .client
            .get(url)
            .header("Authorization", format!("Bearer {}", config.api_key))
            .send()
            .await
        {
            Ok(response) => {
                if !response.status().is_success() {
                    return vec![ProviderUsage {
                        provider_id: self.provider_id().to_string(),
                        provider_name: "OpenCode".to_string(),
                        is_available: false,
                        description: format!("API Error ({})", response.status()),
                        ..Default::default()
                    }];
                }

                let content = match response.text().await {
                    Ok(text) => text,
                    Err(e) => {
                        error!("Failed to read OpenCode response: {}", e);
                        return vec![ProviderUsage {
                            provider_id: self.provider_id().to_string(),
                            provider_name: "OpenCode".to_string(),
                            is_available: false,
                            description: "Failed to read response".to_string(),
                            ..Default::default()
                        }];
                    }
                };

                if content.trim() == "Not Found" {
                    return vec![ProviderUsage {
                        provider_id: self.provider_id().to_string(),
                        provider_name: "OpenCode".to_string(),
                        is_available: false,
                        description: "Service Unavailable".to_string(),
                        ..Default::default()
                    }];
                }

                match serde_json::from_str::<OpenCodeCreditsResponse>(&content) {
                    Ok(data) => {
                        if let Some(credits) = data.data {
                            let total = credits.total_credits;
                            let used = credits.used_credits;
                            let utilization = if total > 0.0 {
                                (used / total) * 100.0
                            } else {
                                0.0
                            };

                            vec![ProviderUsage {
                                provider_id: self.provider_id().to_string(),
                                provider_name: "OpenCode".to_string(),
                                usage_percentage: utilization.min(100.0),
                                cost_used: used,
                                cost_limit: total,
                                usage_unit: "Credits".to_string(),
                                is_quota_based: false,
                                payment_type: PaymentType::Credits,
                                description: format!("{:.2} / {:.2} credits", used, total),
                                ..Default::default()
                            }]
                        } else {
                            vec![ProviderUsage {
                                provider_id: self.provider_id().to_string(),
                                provider_name: "OpenCode".to_string(),
                                is_available: false,
                                description: "Invalid response structure".to_string(),
                                ..Default::default()
                            }]
                        }
                    }
                    Err(e) => {
                        error!("Failed to parse OpenCode response: {}", e);
                        vec![ProviderUsage {
                            provider_id: self.provider_id().to_string(),
                            provider_name: "OpenCode".to_string(),
                            is_available: false,
                            description: format!("Parse error: {}", e),
                            ..Default::default()
                        }]
                    }
                }
            }
            Err(e) => {
                error!("OpenCode request failed: {}", e);
                vec![ProviderUsage {
                    provider_id: self.provider_id().to_string(),
                    provider_name: "OpenCode".to_string(),
                    is_available: false,
                    description: "Connection Failed".to_string(),
                    ..Default::default()
                }]
            }
        }
    }
}