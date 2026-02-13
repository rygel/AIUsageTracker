use crate::models::{PaymentType, ProviderConfig, ProviderUsage};
use crate::provider::ProviderService;
use async_trait::async_trait;
use reqwest::Client;

pub struct MistralProvider {
    client: Client,
}

impl MistralProvider {
    pub fn new(client: Client) -> Self {
        Self { client }
    }
}

#[async_trait]
impl ProviderService for MistralProvider {
    fn provider_id(&self) -> &'static str {
        "mistral"
    }

    async fn get_usage(&self, config: &ProviderConfig) -> Vec<ProviderUsage> {
        if config.api_key.is_empty() {
            return vec![ProviderUsage {
                provider_id: self.provider_id().to_string(),
                provider_name: "Mistral AI".to_string(),
                is_available: false,
                description: "API Key missing".to_string(),
                ..Default::default()
            }];
        }

        let url = "https://api.mistral.ai/v1/models";

        match self
            .client
            .get(url)
            .header("Authorization", format!("Bearer {}", config.api_key))
            .send()
            .await
        {
            Ok(response) => {
                if response.status().is_success() {
                    vec![ProviderUsage {
                        provider_id: self.provider_id().to_string(),
                        provider_name: "Mistral AI".to_string(),
                        is_available: true,
                        usage_percentage: 0.0,
                        is_quota_based: false,
                        payment_type: PaymentType::UsageBased,
                        description: "Connected (Check Dashboard)".to_string(),
                        usage_unit: "Status".to_string(),
                        ..Default::default()
                    }]
                } else {
                    vec![ProviderUsage {
                        provider_id: self.provider_id().to_string(),
                        provider_name: "Mistral AI".to_string(),
                        is_available: false,
                        description: format!("Invalid API Key ({})", response.status()),
                        ..Default::default()
                    }]
                }
            }
            Err(e) => {
                vec![ProviderUsage {
                    provider_id: self.provider_id().to_string(),
                    provider_name: "Mistral AI".to_string(),
                    is_available: false,
                    description: "Connection Failed".to_string(),
                    ..Default::default()
                }]
            }
        }
    }
}