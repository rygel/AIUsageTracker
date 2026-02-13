use crate::models::{PaymentType, ProviderConfig, ProviderUsage, ProviderUsageDetail};
use crate::provider::ProviderService;
use async_trait::async_trait;
use chrono::{DateTime, Utc};
use log::error;
use reqwest::Client;
use serde::Deserialize;

pub struct OpenRouterProvider {
    client: Client,
}

impl OpenRouterProvider {
    pub fn new(client: Client) -> Self {
        Self { client }
    }
}

#[derive(Debug, Deserialize)]
struct OpenRouterCreditsResponse {
    data: Option<CreditsData>,
}

#[derive(Debug, Deserialize)]
struct CreditsData {
    #[serde(rename = "total_credits")]
    total_credits: f64,
    #[serde(rename = "total_usage")]
    total_usage: f64,
}

#[derive(Debug, Deserialize)]
struct OpenRouterKeyResponse {
    data: Option<KeyData>,
}

#[derive(Debug, Deserialize)]
struct KeyData {
    label: Option<String>,
    limit: f64,
    #[serde(rename = "limit_reset")]
    limit_reset: Option<String>,
    #[serde(rename = "is_free_tier")]
    is_free_tier: bool,
}

#[async_trait]
impl ProviderService for OpenRouterProvider {
    fn provider_id(&self) -> &'static str {
        "openrouter"
    }

    async fn get_usage(&self, config: &ProviderConfig) -> Vec<ProviderUsage> {
        if config.api_key.is_empty() {
            return vec![ProviderUsage {
                provider_id: self.provider_id().to_string(),
                provider_name: "OpenRouter".to_string(),
                is_available: false,
                description: "API Key not found".to_string(),
                ..Default::default()
            }];
        }

        // Fetch credits
        let credits_result = self
            .client
            .get("https://openrouter.ai/api/v1/credits")
            .header("Authorization", format!("Bearer {}", config.api_key))
            .send()
            .await;

        match credits_result {
            Ok(response) => {
                if !response.status().is_success() {
                    return vec![ProviderUsage {
                        provider_id: self.provider_id().to_string(),
                        provider_name: "OpenRouter".to_string(),
                        is_available: false,
                        description: format!("API Error ({})", response.status()),
                        ..Default::default()
                    }];
                }

                match response.json::<OpenRouterCreditsResponse>().await {
                    Ok(credits_data) => {
                        let mut details = Vec::new();
                        let mut label = "OpenRouter".to_string();
                        let mut main_reset = String::new();
                        let mut next_reset_time: Option<DateTime<Utc>> = None;

                        // Try to fetch key info for additional details
                        if let Ok(key_response) = self
                            .client
                            .get("https://openrouter.ai/api/v1/key")
                            .header("Authorization", format!("Bearer {}", config.api_key))
                            .send()
                            .await
                        {
                            if let Ok(key_data) = key_response.json::<OpenRouterKeyResponse>().await
                            {
                                if let Some(key) = key_data.data {
                                    if let Some(lbl) = key.label {
                                        label = lbl;
                                    }

                                    if key.limit > 0.0 {
                                        if let Some(reset_str) = key.limit_reset {
                                            if let Ok(dt) = DateTime::parse_from_rfc3339(&reset_str)
                                            {
                                                let diff = dt.with_timezone(&Utc) - Utc::now();
                                                if diff.num_seconds() > 0 {
                                                    main_reset = format!(
                                                        " (Resets: ({}))",
                                                        dt.format("%b %d %H:%M")
                                                    );
                                                    next_reset_time = Some(dt.with_timezone(&Utc));
                                                }
                                            }
                                        }

                                        details.push(ProviderUsageDetail {
                                            name: "Spending Limit".to_string(),
                                            used: String::new(),
                                            remaining: None,
                                            description: format!("{:.2}{}", key.limit, main_reset),
                                            next_reset_time,
                                        });
                                    }

                                    details.push(ProviderUsageDetail {
                                        name: "Free Tier".to_string(),
                                        used: String::new(),
                                        remaining: None,
                                        description: if key.is_free_tier {
                                            "Yes".to_string()
                                        } else {
                                            "No".to_string()
                                        },
                                        next_reset_time: None,
                                    });
                                }
                            }
                        }

                        if let Some(data) = credits_data.data {
                            let total = data.total_credits;
                            let used = data.total_usage;
                            let utilization = if total > 0.0 {
                                (used / total) * 100.0
                            } else {
                                0.0
                            };
                            let remaining = total - used;

                            return vec![ProviderUsage {
                                provider_id: config.provider_id.clone(),
                                provider_name: label,
                                usage_percentage: utilization.min(100.0),
                                cost_used: used,
                                cost_limit: total,
                                payment_type: PaymentType::Credits,
                                usage_unit: "Credits".to_string(),
                                is_quota_based: true,
                                is_available: true,
                                description: format!(
                                    "{:.2} Credits Remaining{}",
                                    remaining, main_reset
                                ),
                                next_reset_time,
                                details: if details.is_empty() {
                                    None
                                } else {
                                    Some(details)
                                },
                                ..Default::default()
                            }];
                        }

                        vec![ProviderUsage {
                            provider_id: self.provider_id().to_string(),
                            provider_name: "OpenRouter".to_string(),
                            is_available: false,
                            description: "Failed to parse credits response".to_string(),
                            ..Default::default()
                        }]
                    }
                    Err(e) => {
                        error!("Failed to parse OpenRouter response: {}", e);
                        vec![ProviderUsage {
                            provider_id: self.provider_id().to_string(),
                            provider_name: "OpenRouter".to_string(),
                            is_available: false,
                            description: "Parsing failed".to_string(),
                            ..Default::default()
                        }]
                    }
                }
            }
            Err(e) => {
                error!("OpenRouter request failed: {}", e);
                vec![ProviderUsage {
                    provider_id: self.provider_id().to_string(),
                    provider_name: "OpenRouter".to_string(),
                    is_available: false,
                    description: "Connection Failed".to_string(),
                    ..Default::default()
                }]
            }
        }
    }
}
