use crate::models::{PaymentType, ProviderConfig, ProviderUsage};
use crate::provider::ProviderService;
use async_trait::async_trait;
use chrono::{DateTime, Utc};
use log::error;
use reqwest::Client;
use serde::Deserialize;

pub struct GenericPayAsYouGoProvider {
    client: Client,
}

impl GenericPayAsYouGoProvider {
    pub fn new(client: Client) -> Self {
        Self { client }
    }

    /// Try to load provider URL from providers.json file
    async fn get_url_from_providers_file(provider_id: &str) -> Option<String> {
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
                        if let Some(url) = providers.get(provider_id) {
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
struct GenericCreditsResponse {
    data: Option<GenericCreditsData>,
}

#[derive(Debug, Deserialize)]
struct GenericCreditsData {
    #[serde(rename = "total_credits")]
    total_credits: f64,
    #[serde(rename = "used_credits")]
    used_credits: f64,
}

#[derive(Debug, Deserialize)]
struct GenericKimiResponse {
    data: Option<GenericKimiData>,
}

#[derive(Debug, Deserialize)]
struct GenericKimiData {
    #[serde(rename = "available_balance")]
    available_balance: f64,
}

#[async_trait]
impl ProviderService for GenericPayAsYouGoProvider {
    fn provider_id(&self) -> &'static str {
        "generic-pay-as-you-go"
    }

    async fn get_usage(&self, config: &ProviderConfig) -> Vec<ProviderUsage> {
        if config.api_key.is_empty() {
            return vec![ProviderUsage {
                provider_id: config.provider_id.clone(),
                provider_name: config.provider_id.clone(),
                is_available: false,
                description: "API Key not found".to_string(),
                ..Default::default()
            }];
        }

        let mut url = config.base_url.clone();

        // Determine URL based on provider_id
        if url.is_none() {
            url = Some(match config.provider_id.as_str() {
                id if id.contains("opencode") => "https://api.opencode.ai/v1/credits".to_string(),
                "minimax" => "https://api.minimax.chat/v1/user/usage".to_string(),
                "xiaomi" => "https://api.xiaomimimo.com/v1/user/balance".to_string(),
                id if id.contains("kilocode") || id == "kilo" => {
                    "https://api.kilocode.ai/v1/credits".to_string()
                }
                _ => {
                    // Try to load URL from providers.json for unknown providers
                    if let Some(providers_url) = Self::get_url_from_providers_file(&config.provider_id).await {
                        providers_url
                    } else {
                        return vec![ProviderUsage {
                            provider_id: config.provider_id.clone(),
                            provider_name: config.provider_id.clone(),
                            is_available: false,
                            description: "Configuration Required (Add 'base_url' to auth.json)"
                                .to_string(),
                            ..Default::default()
                        }];
                    }
                }
            });
        }

        let mut url = url.unwrap();
        if !url.starts_with("http") {
            url = format!("https://{}", url);
        }

        // Add /credits suffix if needed
        if !url.ends_with("/credits")
            && !url.contains("/quota")
            && !url.contains("billing")
            && !url.contains("usage")
            && !url.contains("balance")
        {
            if url.ends_with("/v1") {
                url = format!("{}/credits", url);
            } else {
                url = format!("{}/v1/credits", url.trim_end_matches('/'));
            }
        }

        match self
            .client
            .get(&url)
            .header("Authorization", format!("Bearer {}", config.api_key))
            .send()
            .await
        {
            Ok(response) => {
                if !response.status().is_success() {
                    return vec![ProviderUsage {
                        provider_id: config.provider_id.clone(),
                        provider_name: config.provider_id.clone(),
                        is_available: false,
                        description: format!("API Error ({})", response.status()),
                        ..Default::default()
                    }];
                }

                let response_string = match response.text().await {
                    Ok(s) => s,
                    Err(e) => {
                        error!("Failed to read response: {}", e);
                        return vec![ProviderUsage {
                            provider_id: config.provider_id.clone(),
                            provider_name: config.provider_id.clone(),
                            is_available: false,
                            description: "Failed to read response".to_string(),
                            ..Default::default()
                        }];
                    }
                };

                if response_string.trim().eq_ignore_ascii_case("Not Found") {
                    return vec![ProviderUsage {
                        provider_id: config.provider_id.clone(),
                        provider_name: config.provider_id.clone(),
                        is_available: true,
                        description: "Not Found (Invalid Key/URL)".to_string(),
                        ..Default::default()
                    }];
                }

                // Try different response formats
                let mut total = 0.0;
                let mut used = 0.0;
                let mut payment_type = PaymentType::UsageBased;
                let mut next_reset_time: Option<DateTime<Utc>> = None;
                let mut format_matched = false;

                // Try OpenCode format
                if let Ok(data) = serde_json::from_str::<GenericCreditsResponse>(&response_string) {
                    if let Some(credits) = data.data {
                        total = credits.total_credits;
                        used = credits.used_credits;
                        payment_type = PaymentType::Credits;
                        format_matched = true;
                    }
                }

                // Try Kimi format (only if OpenCode didn't match)
                if !format_matched {
                    if let Ok(data) = serde_json::from_str::<GenericKimiResponse>(&response_string)
                    {
                        if let Some(kimi_data) = data.data {
                            total = kimi_data.available_balance;
                            used = 0.0;
                            payment_type = PaymentType::Credits;
                            format_matched = true;
                        }
                    }
                }

                if !format_matched {
                    return vec![ProviderUsage {
                        provider_id: config.provider_id.clone(),
                        provider_name: config.provider_id.clone(),
                        is_available: false,
                        description: "Unknown response format".to_string(),
                        ..Default::default()
                    }];
                }

                let utilization = if total > 0.0 {
                    (used / total) * 100.0
                } else {
                    0.0
                };
                let reset_str = if next_reset_time.is_some() {
                    format!(
                        " (Resets: ({}))",
                        next_reset_time.unwrap().format("%b %d %H:%M")
                    )
                } else {
                    String::new()
                };

                let display_name = if config.provider_id == "generic-pay-as-you-go" {
                    url.replace("https://", "")
                        .replace("/v1/credits", "")
                        .replace("/credits", "")
                } else {
                    config.provider_id.clone()
                };

                // Title case the name
                let display_name = display_name
                    .split(|c| c == '-' || c == '.' || c == ' ')
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

                // Determine if this is a quota-based/coding plan (has reset time)
                let is_quota = next_reset_time.is_some() && matches!(payment_type, PaymentType::Quota);
                
                let usage_unit = if is_quota { 
                    "Quota %".to_string() 
                } else { 
                    "Credits".to_string() 
                };
                
                vec![ProviderUsage {
                    provider_id: config.provider_id.clone(),
                    provider_name: display_name,
                    usage_percentage: utilization.min(100.0),
                    cost_used: used,
                    cost_limit: total,
                    payment_type,
                    usage_unit,
                    is_quota_based: is_quota,
                    description: format!("{:.2} / {:.2} {}", used, total, if is_quota { "%" } else { "credits" }),
                    next_reset_time,
                    raw_response: Some(response_string),
                    ..Default::default()
                }]
            }
            Err(e) => {
                error!("Generic provider request failed: {}", e);
                vec![ProviderUsage {
                    provider_id: config.provider_id.clone(),
                    provider_name: config.provider_id.clone(),
                    is_available: false,
                    description: "Connection Failed".to_string(),
                    ..Default::default()
                }]
            }
        }
    }
}
