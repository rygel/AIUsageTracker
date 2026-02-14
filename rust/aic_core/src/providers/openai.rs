use crate::models::{PaymentType, ProviderConfig, ProviderUsage};
use crate::provider::ProviderService;
use async_trait::async_trait;
use log::error;
use reqwest::Client;

pub struct OpenAIProvider {
    client: Client,
}

impl OpenAIProvider {
    pub fn new(client: Client) -> Self {
        Self { client }
    }
}

#[async_trait]
impl ProviderService for OpenAIProvider {
    fn provider_id(&self) -> &'static str {
        "openai"
    }

    async fn get_usage(&self, config: &ProviderConfig) -> Vec<ProviderUsage> {
        if config.api_key.is_empty() {
            return vec![ProviderUsage {
                provider_id: self.provider_id().to_string(),
                provider_name: "OpenAI".to_string(),
                is_available: false,
                description: "API Key is missing via environment variable or auth.json".to_string(),
                ..Default::default()
            }];
        }

        if config.api_key.starts_with("sk-proj") {
            return vec![ProviderUsage {
                provider_id: self.provider_id().to_string(),
                provider_name: "OpenAI".to_string(),
                is_available: false,
                description:
                    "Project keys (sk-proj-...) not supported yet. Use a standard user API key."
                        .to_string(),
                ..Default::default()
            }];
        }

        match self
            .client
            .get("https://api.openai.com/v1/models")
            .header("Authorization", format!("Bearer {}", config.api_key))
            .send()
            .await
        {
            Ok(response) => {
                let status = response.status();
                let raw_body = response.text().await.unwrap_or_else(|_| "Failed to read body".to_string());
                
                if status.is_success() {
                    vec![ProviderUsage {
                        provider_id: self.provider_id().to_string(),
                        provider_name: "OpenAI".to_string(),
                        is_available: true,
                        usage_percentage: 0.0,
                        is_quota_based: false,
                        payment_type: PaymentType::UsageBased,
                        description: "Connected (Check Dashboard)".to_string(),
                        usage_unit: "Status".to_string(),
                        raw_response: Some(raw_body),
                        ..Default::default()
                    }]
                } else {
                    vec![ProviderUsage {
                        provider_id: self.provider_id().to_string(),
                        provider_name: "OpenAI".to_string(),
                        is_available: false,
                        description: format!("Invalid Key ({})", status),
                        raw_response: Some(raw_body),
                        ..Default::default()
                    }]
                }
            }
            Err(e) => {
                error!("OpenAI check failed: {}", e);
                vec![ProviderUsage {
                    provider_id: self.provider_id().to_string(),
                    provider_name: "OpenAI".to_string(),
                    is_available: false,
                    description: "Connection Failed".to_string(),
                    ..Default::default()
                }]
            }
        }
    }
}
