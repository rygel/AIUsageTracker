use crate::models::{PaymentType, ProviderConfig, ProviderUsage};
use crate::provider::ProviderService;
use async_trait::async_trait;
use chrono::{DateTime, Local, Utc};
use log::error;
use reqwest::Client;
use serde::Deserialize;

pub struct ZaiProvider {
    client: Client,
}

impl ZaiProvider {
    pub fn new(client: Client) -> Self {
        Self { client }
    }
}

#[derive(Debug, Deserialize)]
struct ZaiEnvelope<T> {
    data: Option<T>,
}

#[derive(Debug, Deserialize)]
struct ZaiQuotaLimitResponse {
    limits: Option<Vec<ZaiQuotaLimitItem>>,
}

#[derive(Debug, Deserialize)]
struct ZaiQuotaLimitItem {
    #[serde(rename = "type")]
    limit_type: Option<String>,
    percentage: Option<f64>,
    #[serde(rename = "currentValue")]
    current_value: Option<i64>,
    #[serde(rename = "usage")]
    total: Option<i64>,
    remaining: Option<i64>,
}

#[async_trait]
impl ProviderService for ZaiProvider {
    fn provider_id(&self) -> &'static str {
        "zai-coding-plan"
    }

    async fn get_usage(&self, config: &ProviderConfig) -> Vec<ProviderUsage> {
        if config.api_key.is_empty() {
            return vec![ProviderUsage {
                provider_id: self.provider_id().to_string(),
                provider_name: "Z.AI".to_string(),
                is_available: false,
                description: "API Key not found".to_string(),
                ..Default::default()
            }];
        }

        match self
            .client
            .get("https://api.z.ai/api/monitor/usage/quota/limit")
            .header("Authorization", &config.api_key)
            .header("Accept-Language", "en-US,en")
            .send()
            .await
        {
            Ok(response) => {
                if !response.status().is_success() {
                    return vec![ProviderUsage {
                        provider_id: self.provider_id().to_string(),
                        provider_name: "Z.AI".to_string(),
                        is_available: false,
                        description: format!("API Error ({})", response.status()),
                        ..Default::default()
                    }];
                }

                match response.json::<ZaiEnvelope<ZaiQuotaLimitResponse>>().await {
                    Ok(envelope) => {
                        let limits = envelope.data.and_then(|d| d.limits);

                        if limits.is_none() || limits.as_ref().unwrap().is_empty() {
                            return vec![ProviderUsage {
                                provider_id: self.provider_id().to_string(),
                                provider_name: "Z.AI".to_string(),
                                is_available: false,
                                description: "No usage limits found".to_string(),
                                ..Default::default()
                            }];
                        }

                        let limits = limits.unwrap();
                        let token_limit = limits.iter().find(|l| {
                            l.limit_type
                                .as_ref()
                                .map(|t| t.to_uppercase() == "TOKENS_LIMIT")
                                .unwrap_or(false)
                        });

                        let mcp_limit = limits.iter().find(|l| {
                            l.limit_type
                                .as_ref()
                                .map(|t| t.to_uppercase() == "TIME_LIMIT")
                                .unwrap_or(false)
                        });

                        let mut used_percent: f64 = 0.0;
                        let mut detail_info = String::new();
                        let mut plan_description = "API".to_string();

                        if let Some(token) = token_limit {
                            plan_description = "Coding Plan".to_string();

                            let limit_percent = token.percentage.unwrap_or_else(|| {
                                if let (Some(current), Some(total)) =
                                    (token.current_value, token.total)
                                {
                                    if total > 0 {
                                        (current as f64 / total as f64) * 100.0
                                    } else {
                                        0.0
                                    }
                                } else {
                                    0.0
                                }
                            });

                            used_percent = used_percent.max(limit_percent);

                            if let Some(total) = token.total {
                                if total > 50_000_000 {
                                    plan_description = "Coding Plan (Ultra/Enterprise)".to_string();
                                } else if total > 10_000_000 {
                                    plan_description = "Coding Plan (Pro)".to_string();
                                }
                                detail_info = format!(
                                    "{:.1}% of {:.0}M tokens used",
                                    limit_percent,
                                    total as f64 / 1_000_000.0
                                );
                            }
                        }

                        if let Some(mcp) = mcp_limit {
                            if mcp.percentage.unwrap_or(0.0) > 0.0 {
                                used_percent = used_percent.max(mcp.percentage.unwrap());
                            }
                        }

                        // Z.AI resets at UTC midnight - convert to local time for display
                        let reset_dt_utc = Utc::now()
                            .date_naive()
                            .and_hms_opt(0, 0, 0)
                            .unwrap_or_else(|| Utc::now().naive_utc())
                            + chrono::Duration::days(1);
                        let reset_datetime_utc = DateTime::from_naive_utc_and_offset(reset_dt_utc, Utc);
                        let reset_datetime_local = reset_datetime_utc.with_timezone(&Local);
                        let z_reset =
                            format!(" (Resets: ({}))", reset_datetime_local.format("%b %d %H:%M"));

                        let remaining_percent = 100.0 - used_percent.min(100.0);
                        
                        vec![ProviderUsage {
                            provider_id: self.provider_id().to_string(),
                            provider_name: format!("Z.AI {}", plan_description),
                            usage_percentage: used_percent.min(100.0),
                            remaining_percentage: Some(remaining_percent),
                            cost_used: used_percent,
                            cost_limit: 100.0,
                            usage_unit: "Quota %".to_string(),
                            is_quota_based: true,
                            payment_type: PaymentType::Quota,
                            description: format!(
                                "{}{}",
                                if detail_info.is_empty() {
                                    format!("{:.1}% utilized", used_percent)
                                } else {
                                    detail_info
                                },
                                z_reset
                            ),
                            next_reset_time: Some(reset_datetime_utc),
                            ..Default::default()
                        }]
                    }
                    Err(e) => {
                        error!("Failed to parse Z.AI response: {}", e);
                        vec![ProviderUsage {
                            provider_id: self.provider_id().to_string(),
                            provider_name: "Z.AI".to_string(),
                            is_available: false,
                            description: "Parsing failed".to_string(),
                            ..Default::default()
                        }]
                    }
                }
            }
            Err(e) => {
                error!("Z.AI request failed: {}", e);
                vec![ProviderUsage {
                    provider_id: self.provider_id().to_string(),
                    provider_name: "Z.AI".to_string(),
                    is_available: false,
                    description: "Connection Failed".to_string(),
                    ..Default::default()
                }]
            }
        }
    }
}
