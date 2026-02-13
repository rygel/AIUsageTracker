use crate::models::{PaymentType, ProviderConfig, ProviderUsage, ProviderUsageDetail};
use crate::provider::ProviderService;
use async_trait::async_trait;
use chrono::{DateTime, Local, Utc};
use log::{error, warn};
use reqwest::Client;
use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::path::PathBuf;

pub struct GeminiProvider {
    client: Client,
}

impl GeminiProvider {
    pub fn new(client: Client) -> Self {
        Self { client }
    }

    fn load_antigravity_accounts(&self) -> Option<AntigravityAccounts> {
        let path = directories::BaseDirs::new()
            .map(|base| {
                base.home_dir()
                    .join(".config")
                    .join("opencode")
                    .join("antigravity-accounts.json")
            })
            .unwrap_or_else(|| PathBuf::from(".config/opencode/antigravity-accounts.json"));

        if !path.exists() {
            return None;
        }

        match std::fs::read_to_string(&path) {
            Ok(json) => match serde_json::from_str::<AntigravityAccounts>(&json) {
                Ok(accounts) => Some(accounts),
                Err(e) => {
                    error!("Failed to parse antigravity-accounts.json: {}", e);
                    None
                }
            },
            Err(e) => {
                error!("Failed to read antigravity-accounts.json: {}", e);
                None
            }
        }
    }

    async fn refresh_token(
        &self,
        refresh_token: &str,
    ) -> Result<String, Box<dyn std::error::Error>> {
        let mut params = HashMap::new();
        params.insert(
            "client_id",
            "1071006060591-tmhssin2h21lcre235vtolojh4g403ep.apps.googleusercontent.com",
        );
        params.insert("client_secret", "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");
        params.insert("refresh_token", refresh_token);
        params.insert("grant_type", "refresh_token");

        let response = self
            .client
            .post("https://oauth2.googleapis.com/token")
            .form(&params)
            .send()
            .await?;

        if !response.status().is_success() {
            return Err(format!("Token refresh failed: {}", response.status()).into());
        }

        let token_response: GeminiTokenResponse = response.json().await?;
        token_response
            .access_token
            .ok_or_else(|| "Failed to retrieve access token".into())
    }

    async fn fetch_quota(
        &self,
        access_token: &str,
        project_id: &str,
    ) -> Result<Vec<Bucket>, Box<dyn std::error::Error>> {
        let body = serde_json::json!({
            "project": project_id
        });

        let response = self
            .client
            .post("https://cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota")
            .header("Authorization", format!("Bearer {}", access_token))
            .json(&body)
            .send()
            .await?;

        if !response.status().is_success() {
            return Err(format!("Quota fetch failed: {}", response.status()).into());
        }

        let quota_response: GeminiQuotaResponse = response.json().await?;
        Ok(quota_response.buckets.unwrap_or_default())
    }
}

#[async_trait]
impl ProviderService for GeminiProvider {
    fn provider_id(&self) -> &'static str {
        "gemini-cli"
    }

    async fn get_usage(&self, _config: &ProviderConfig) -> Vec<ProviderUsage> {
        let accounts = match self.load_antigravity_accounts() {
            Some(acc) if !acc.accounts.is_empty() => acc.accounts,
            _ => {
                return vec![ProviderUsage {
                    provider_id: self.provider_id().to_string(),
                    provider_name: "Gemini CLI".to_string(),
                    is_available: false,
                    is_quota_based: false,
                    payment_type: PaymentType::Credits,
                    description: "No Gemini accounts found".to_string(),
                    ..Default::default()
                }];
            }
        };

        let mut results = Vec::new();

        for account in accounts {
            match self.process_account(&account).await {
                Ok(usage) => results.push(usage),
                Err(e) => {
                    warn!("Failed to fetch Gemini quota for {}: {}", account.email, e);
                    results.push(ProviderUsage {
                        provider_id: self.provider_id().to_string(),
                        provider_name: "Gemini CLI".to_string(),
                        is_available: true,
                        description: format!("Error: {}", e),
                        account_name: account.email,
                        ..Default::default()
                    });
                }
            }
        }

        if results.is_empty() {
            vec![ProviderUsage {
                provider_id: self.provider_id().to_string(),
                provider_name: "Gemini CLI".to_string(),
                is_available: false,
                description: "Failed to fetch quota for any account".to_string(),
                ..Default::default()
            }]
        } else {
            results
        }
    }
}

impl GeminiProvider {
    async fn process_account(
        &self,
        account: &Account,
    ) -> Result<ProviderUsage, Box<dyn std::error::Error>> {
        let access_token = self.refresh_token(&account.refresh_token).await?;
        let buckets = self.fetch_quota(&access_token, &account.project_id).await?;

        let mut min_frac: f64 = 1.0;
        let mut main_reset_str = String::new();
        let mut soonest_reset_dt: Option<DateTime<Utc>> = None;
        let mut details = Vec::new();

        for bucket in &buckets {
            min_frac = min_frac.min(bucket.remaining_fraction);

            let name = if let Some(data) = &bucket.extension_data {
                data.get("quotaId")
                    .and_then(|v| v.as_str())
                    .map(|s| {
                        let s = regex::Regex::new(r"([a-z])([A-Z])")
                            .unwrap()
                            .replace_all(s, "$1 $2")
                            .to_string();
                        s.replace("Requests Per Day", "(Day)")
                            .replace("Requests Per Minute", "(Min)")
                    })
                    .unwrap_or_else(|| "Quota Bucket".to_string())
            } else {
                "Quota Bucket".to_string()
            };

            let used = 100.0 - (bucket.remaining_fraction * 100.0);

            let mut reset_time = bucket.reset_time.clone();

            // Infer reset time from quotaId if not provided
            if reset_time.is_none() {
                if let Some(data) = &bucket.extension_data {
                    if let Some(qid) = data.get("quotaId").and_then(|v| v.as_str()) {
                        if qid.to_lowercase().contains("requestsperday") {
                            reset_time = Some(
                                Utc::now()
                                    .date_naive()
                                    .succ_opt()
                                    .unwrap_or_else(|| Utc::now().date_naive())
                                    .and_hms_opt(0, 0, 0)
                                    .unwrap()
                                    .to_string(),
                            );
                        } else if qid.to_lowercase().contains("requestsperminute") {
                            reset_time =
                                Some((Utc::now() + chrono::Duration::minutes(1)).to_rfc3339());
                        }
                    }
                }
            }

            let mut reset_str = String::new();
            let mut item_reset_dt: Option<DateTime<Utc>> = None;

            if let Some(ref rt) = reset_time {
                if let Ok(dt) = DateTime::parse_from_rfc3339(rt) {
                    let local_dt = dt.with_timezone(&Local);
                    let diff = local_dt - Local::now();
                    if diff.num_seconds() > 0 {
                        reset_str = format!(" (Resets: ({}))", local_dt.format("%b %d %H:%M"));
                        item_reset_dt = Some(dt.with_timezone(&Utc));
                    }
                }
            }

            let used_pct = used * 100.0;
            let remaining_pct = bucket.remaining_fraction * 100.0;
            
            details.push(ProviderUsageDetail {
                name,
                used: format!("{:.1}%", used_pct),
                remaining: Some(remaining_pct),
                description: format!(
                    "{:.1}% remaining{}",
                    remaining_pct,
                    reset_str
                ),
                next_reset_time: item_reset_dt,
            });
        }

        // Sort details by name
        details.sort_by(|a, b| a.name.cmp(&b.name));

        let used_percentage = 100.0 - (min_frac * 100.0);

        // Find soonest reset
        if let Some(soonest) = buckets
            .iter()
            .filter(|b| b.reset_time.is_some())
            .min_by_key(|b| {
                b.reset_time
                    .as_ref()
                    .and_then(|rt| DateTime::parse_from_rfc3339(rt).ok())
                    .map(|dt| dt.timestamp())
                    .unwrap_or(i64::MAX)
            })
        {
            if let Some(ref rt) = soonest.reset_time {
                if let Ok(dt) = DateTime::parse_from_rfc3339(rt) {
                    let local_dt = dt.with_timezone(&Local);
                    let diff = local_dt - Local::now();
                    if diff.num_seconds() > 0 {
                        main_reset_str = format!(" (Resets: ({}))", local_dt.format("%b %d %H:%M"));
                        soonest_reset_dt = Some(dt.with_timezone(&Utc));
                    }
                }
            }
        }

        let remaining_percentage = min_frac * 100.0;
        
        Ok(ProviderUsage {
            provider_id: self.provider_id().to_string(),
            provider_name: "Gemini CLI".to_string(),
            usage_percentage: used_percentage,
            remaining_percentage: Some(remaining_percentage),
            cost_used: used_percentage,
            cost_limit: 100.0,
            usage_unit: "Quota %".to_string(),
            is_quota_based: true,
            payment_type: PaymentType::Quota,
            account_name: account.email.clone(),
            description: format!("{:.1}% Used{}", used_percentage, main_reset_str),
            next_reset_time: soonest_reset_dt,
            details: if details.is_empty() {
                None
            } else {
                Some(details)
            },
            ..Default::default()
        })
    }
}

// Data models
#[derive(Debug, Deserialize)]
struct AntigravityAccounts {
    #[serde(default)]
    accounts: Vec<Account>,
}

#[derive(Debug, Deserialize)]
struct Account {
    email: String,
    #[serde(rename = "refreshToken")]
    refresh_token: String,
    #[serde(rename = "projectId")]
    project_id: String,
}

#[derive(Debug, Deserialize)]
struct GeminiTokenResponse {
    #[serde(rename = "access_token")]
    access_token: Option<String>,
}

#[derive(Debug, Deserialize)]
struct GeminiQuotaResponse {
    buckets: Option<Vec<Bucket>>,
}

#[derive(Debug, Deserialize)]
struct Bucket {
    #[serde(rename = "remainingFraction")]
    remaining_fraction: f64,
    #[serde(rename = "resetTime")]
    reset_time: Option<String>,
    #[serde(flatten)]
    extension_data: Option<serde_json::Value>,
}
