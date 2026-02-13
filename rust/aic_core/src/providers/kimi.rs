use crate::models::{PaymentType, ProviderConfig, ProviderUsage, ProviderUsageDetail};
use crate::provider::ProviderService;
use async_trait::async_trait;
use chrono::{DateTime, Utc};
use log::error;
use reqwest::Client;
use serde::Deserialize;

pub struct KimiProvider {
    client: Client,
}

impl KimiProvider {
    pub fn new(client: Client) -> Self {
        Self { client }
    }

    fn format_duration(&self, duration: i64, unit: &str) -> String {
        match unit {
            "TIME_UNIT_MINUTE" => {
                if duration == 60 {
                    "Hourly".to_string()
                } else {
                    format!("{}m", duration)
                }
            }
            "TIME_UNIT_HOUR" => format!("{}h", duration),
            "TIME_UNIT_DAY" => format!("{}d", duration),
            _ => unit.to_string(),
        }
    }
}

#[derive(Debug, Deserialize)]
struct KimiUsageResponse {
    usage: Option<KimiUsageData>,
    limits: Option<Vec<KimiLimitItem>>,
}

#[derive(Debug, Deserialize)]
struct KimiUsageData {
    limit: i64,
    used: i64,
    remaining: i64,
    #[serde(rename = "resetTime")]
    reset_time: Option<String>,
}

#[derive(Debug, Deserialize)]
struct KimiLimitItem {
    window: Option<KimiWindow>,
    detail: Option<KimiLimitDetail>,
}

#[derive(Debug, Deserialize)]
struct KimiWindow {
    duration: i64,
    #[serde(rename = "timeUnit")]
    time_unit: Option<String>,
}

#[derive(Debug, Deserialize)]
struct KimiLimitDetail {
    limit: i64,
    remaining: i64,
    #[serde(rename = "resetTime")]
    reset_time: Option<String>,
}

#[async_trait]
impl ProviderService for KimiProvider {
    fn provider_id(&self) -> &'static str {
        "kimi"
    }

    async fn get_usage(&self, config: &ProviderConfig) -> Vec<ProviderUsage> {
        if config.api_key.is_empty() {
            return vec![ProviderUsage {
                provider_id: self.provider_id().to_string(),
                provider_name: "Kimi".to_string(),
                is_available: false,
                description: "API Key missing".to_string(),
                ..Default::default()
            }];
        }

        match self
            .client
            .get("https://api.kimi.com/coding/v1/usages")
            .header("Authorization", format!("Bearer {}", config.api_key))
            .send()
            .await
        {
            Ok(response) => {
                if !response.status().is_success() {
                    return vec![ProviderUsage {
                        provider_id: self.provider_id().to_string(),
                        provider_name: "Kimi".to_string(),
                        is_available: false,
                        description: format!("API Error ({})", response.status()),
                        ..Default::default()
                    }];
                }

                match response.json::<KimiUsageResponse>().await {
                    Ok(data) => {
                        if let Some(usage) = data.usage {
                            let used = usage.used as f64;
                            let limit = usage.limit as f64;
                            let remaining = usage.remaining as f64;

                            let used_percentage = if limit > 0.0 {
                                100.0 - ((remaining / limit) * 100.0)
                            } else {
                                0.0
                            };

                            let mut description = format!(
                                "{:.1}% Used ({:.0}/{:.0})",
                                used_percentage, remaining, limit
                            );
                            if limit == 0.0 {
                                description = "Unlimited / Pay-as-you-go".to_string();
                            }

                            let mut details = Vec::new();
                            let mut soonest_reset: Option<DateTime<Utc>> = None;

                            if let Some(limits) = data.limits {
                                for limit_item in limits {
                                    if let (Some(window), Some(detail)) =
                                        (limit_item.window, limit_item.detail)
                                    {
                                        if detail.limit <= 0 {
                                            continue;
                                        }

                                        let name = format!(
                                            "{} Limit",
                                            self.format_duration(
                                                window.duration,
                                                window
                                                    .time_unit
                                                    .as_deref()
                                                    .unwrap_or("TIME_UNIT_MINUTE")
                                            )
                                        );
                                        let item_used_pct = 100.0
                                            - ((detail.remaining as f64 / detail.limit as f64)
                                                * 100.0);
                                        let item_remaining_pct = (detail.remaining as f64 / detail.limit as f64) * 100.0;

                                        let item_reset = detail.reset_time.and_then(|s| {
                                            DateTime::parse_from_rfc3339(&s)
                                                .ok()
                                                .map(|dt| dt.with_timezone(&Utc))
                                        });

                                        if let Some(dt) = item_reset {
                                            if soonest_reset.is_none()
                                                || dt < soonest_reset.unwrap()
                                            {
                                                soonest_reset = Some(dt);
                                            }
                                        }

                                        details.push(ProviderUsageDetail {
                                            name,
                                            used: format!("{:.1}%", item_used_pct),
                                            remaining: Some(item_remaining_pct),
                                            description: format!("{} remaining", detail.remaining),
                                            next_reset_time: item_reset,
                                        });
                                    }
                                }
                            }

                            let remaining_percentage = if limit > 0.0 {
                                (remaining / limit) * 100.0
                            } else {
                                100.0
                            };
                            
                            return vec![ProviderUsage {
                                provider_id: self.provider_id().to_string(),
                                provider_name: "Kimi".to_string(),
                                usage_percentage: used_percentage,
                                remaining_percentage: Some(remaining_percentage),
                                cost_used: used,
                                cost_limit: limit,
                                usage_unit: "Points".to_string(),
                                is_quota_based: true,
                                payment_type: PaymentType::Quota,
                                is_available: true,
                                description,
                                details: if details.is_empty() {
                                    None
                                } else {
                                    Some(details)
                                },
                                next_reset_time: soonest_reset,
                                ..Default::default()
                            }];
                        }

                        vec![ProviderUsage {
                            provider_id: self.provider_id().to_string(),
                            provider_name: "Kimi".to_string(),
                            is_available: false,
                            description: "Invalid response structure".to_string(),
                            ..Default::default()
                        }]
                    }
                    Err(e) => {
                        error!("Failed to parse Kimi response: {}", e);
                        vec![ProviderUsage {
                            provider_id: self.provider_id().to_string(),
                            provider_name: "Kimi".to_string(),
                            is_available: false,
                            description: "Parsing failed".to_string(),
                            ..Default::default()
                        }]
                    }
                }
            }
            Err(e) => {
                error!("Kimi request failed: {}", e);
                vec![ProviderUsage {
                    provider_id: self.provider_id().to_string(),
                    provider_name: "Kimi".to_string(),
                    is_available: false,
                    description: "Connection Failed".to_string(),
                    ..Default::default()
                }]
            }
        }
    }
}
