use crate::models::{PaymentType, ProviderConfig, ProviderUsage};
use crate::provider::ProviderService;
use async_trait::async_trait;

pub struct AnthropicProvider;

#[async_trait]
impl ProviderService for AnthropicProvider {
    fn provider_id(&self) -> &'static str {
        "anthropic"
    }

    async fn get_usage(&self, config: &ProviderConfig) -> Vec<ProviderUsage> {
        if config.api_key.is_empty() {
            return vec![ProviderUsage {
                provider_id: self.provider_id().to_string(),
                provider_name: "Claude Code".to_string(),
                is_available: false,
                description: "API Key missing".to_string(),
                ..Default::default()
            }];
        }

        vec![ProviderUsage {
            provider_id: self.provider_id().to_string(),
            provider_name: "Claude Code".to_string(),
            is_available: true,
            usage_percentage: 0.0,
            is_quota_based: false,
            payment_type: PaymentType::UsageBased,
            description: "Connected (Check Dashboard)".to_string(),
            usage_unit: "Status".to_string(),
            ..Default::default()
        }]
    }
}
