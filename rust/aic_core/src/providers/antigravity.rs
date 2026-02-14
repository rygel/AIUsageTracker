use crate::models::{PaymentType, ProviderConfig, ProviderUsage, ProviderUsageDetail};
use crate::provider::ProviderService;
use async_trait::async_trait;
use chrono::{DateTime, Local, Utc};
#[cfg(windows)]
use log::error;
use log::{debug, info, warn};
use reqwest::Client;
use serde::Deserialize;
use std::collections::HashMap;
use std::process::Command;
use std::time::Duration;

pub struct AntigravityProvider {
    client: Client,
}

impl AntigravityProvider {
    pub fn new() -> Self {
        // Create client with SSL certificate validation bypass for localhost
        let client = reqwest::Client::builder()
            .danger_accept_invalid_certs(true)
            .timeout(Duration::from_secs(10))
            .build()
            .unwrap_or_else(|_| Client::new());

        Self { client }
    }

    #[cfg(windows)]
    fn find_process_infos(&self) -> Vec<(u32, String)> {
        use wmi::COMLibrary;
        use wmi::WMIConnection;

        let mut candidates = Vec::new();

        let com_lib = match COMLibrary::new() {
            Ok(lib) => lib,
            Err(e) => {
                error!("Failed to initialize COM: {}", e);
                return candidates;
            }
        };

        let wmi_con = match WMIConnection::new(com_lib) {
            Ok(con) => con,
            Err(e) => {
                error!("Failed to connect to WMI: {}", e);
                return candidates;
            }
        };

        // Query for language server processes
        let query = "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name LIKE '%language_server_windows%'";

        type ProcessInfo = HashMap<String, wmi::Variant>;

        match wmi_con.raw_query::<ProcessInfo>(query) {
            Ok(results) => {
                for process in results {
                    // Extract ProcessId
                    let pid = process.get("ProcessId").and_then(|v| -> Option<u32> {
                        match v {
                            wmi::Variant::UI4(val) => Some(*val),
                            wmi::Variant::I4(val) => Some(*val as u32),
                            _ => None,
                        }
                    });

                    // Extract CommandLine
                    let cmd_line = process.get("CommandLine").and_then(|v| match v {
                        wmi::Variant::String(s) => Some(s.to_string()),
                        _ => None,
                    }) as Option<String>;

                    // Check if it's an antigravity process
                    if let Some(pid_val) = pid {
                        match &cmd_line {
                            Some(cmd_str) => {
                                let cmd: &String = cmd_str;
                                if cmd.contains("antigravity") {
                                    // Extract CSRF token from command line
                                    let re =
                                        regex::Regex::new(r"--csrf_token[=\s]+([a-zA-Z0-9-]+)")
                                            .unwrap();
                                    if let Some(caps) = re.captures(cmd) {
                                        if let Some(token) = caps.get(1) {
                                            candidates.push((pid_val, token.as_str().to_string()));
                                        }
                                    }
                                }
                            }
                            _ => {}
                        }
                    }
                }
            }
            Err(e) => {
                error!("WMI query failed: {}", e);
            }
        }

        candidates
    }

    #[cfg(not(windows))]
    fn find_process_infos(&self) -> Vec<(u32, String)> {
        // Antigravity is Windows-only
        Vec::new()
    }

    #[cfg(windows)]
    fn find_listening_port(&self, pid: u32) -> Option<u16> {
        // Run netstat -ano and parse output
        let output = match Command::new("netstat").args(["-ano"]).output() {
            Ok(output) => output,
            Err(e) => {
                error!("Failed to run netstat: {}", e);
                return None;
            }
        };

        let stdout = String::from_utf8_lossy(&output.stdout);
        let pattern = format!(
            r"\s+TCP\s+(?:127\.0\.0\.1|\[::1\]):(\d+)\s+.*LISTENING\s+{}",
            pid
        );
        let re = regex::Regex::new(&pattern).unwrap();

        for line in stdout.lines() {
            if let Some(caps) = re.captures(line) {
                if let Some(port_match) = caps.get(1) {
                    if let Ok(port) = port_match.as_str().parse::<u16>() {
                        return Some(port);
                    }
                }
            }
        }

        None
    }

    #[cfg(windows)]
    async fn fetch_usage(
        &self,
        port: u16,
        csrf_token: &str,
    ) -> Result<ProviderUsage, Box<dyn std::error::Error>> {
        let url = format!(
            "https://127.0.0.1:{}/exa.language_server_pb.LanguageServerService/GetUserStatus",
            port
        );

        let body = serde_json::json!({
            "metadata": {
                "ideName": "antigravity",
                "extensionName": "antigravity",
                "ideVersion": "unknown",
                "locale": "en"
            }
        });

        let response = self
            .client
            .post(&url)
            .header("X-Codeium-Csrf-Token", csrf_token)
            .header("Connect-Protocol-Version", "1")
            .header("Content-Type", "application/json")
            .json(&body)
            .send()
            .await?;

        if !response.status().is_success() {
            return Err(format!("HTTP error: {}", response.status()).into());
        }

        let response_text = response.text().await?;
        let data: AntigravityResponse = serde_json::from_str(&response_text)?;

        let user_status = data.user_status.ok_or("Missing userStatus in response")?;
        let email = user_status.email.unwrap_or_default();

        let model_configs = user_status
            .cascade_model_config_data
            .as_ref()
            .and_then(|d| d.client_model_configs.as_ref())
            .cloned()
            .unwrap_or_default();

        let model_sorts = user_status
            .cascade_model_config_data
            .as_ref()
            .and_then(|d| d.client_model_sorts.as_ref())
            .and_then(|s| s.first().cloned());

        let master_model_labels: Vec<String> = model_sorts
            .map(|sort| {
                sort.groups
                    .unwrap_or_default()
                    .into_iter()
                    .flat_map(|g| g.model_labels.unwrap_or_default())
                    .collect::<std::collections::HashSet<_>>()
                    .into_iter()
                    .collect()
            })
            .unwrap_or_default();

        let mut details = Vec::new();
        let mut min_remaining: f64 = 100.0;

        // Create config map
        let config_map: std::collections::HashMap<String, ClientModelConfig> = model_configs
            .into_iter()
            .filter(|c| c.label.is_some())
            .map(|c| (c.label.clone().unwrap(), c))
            .collect();

        // Process all model labels
        for label in master_model_labels {
            let mut remaining_pct: f64 = 0.0; // Assume exhausted
            let mut item_reset_dt: Option<DateTime<Utc>> = None;

            if let Some(config) = config_map.get(&label) {
                if let Some(ref quota_info) = config.quota_info {
                    if let Some(fraction) = quota_info.remaining_fraction {
                        remaining_pct = fraction * 100.0;
                    } else if let Some(total) = quota_info.total_requests {
                        if total > 0 {
                            let used = quota_info.used_requests.unwrap_or(0);
                            let remaining = std::cmp::max(0, total - used);
                            remaining_pct = (remaining as f64 / total as f64) * 100.0;
                        }
                    }

                    // Parse reset time
                    if let Some(ref reset_time) = quota_info.reset_time {
                        if let Ok(dt) = DateTime::parse_from_rfc3339(reset_time) {
                            let local_dt = dt.with_timezone(&Local);
                            let diff = local_dt - Local::now();
                            if diff.num_seconds() > 0 {
                                item_reset_dt = Some(dt.with_timezone(&Utc));
                            }
                        }
                    }
                }
            }

            // Calculate used percentage for consistency with other providers
            let detail_used_pct = 100.0 - remaining_pct;

            details.push(ProviderUsageDetail {
                name: label,
                used: format!("{:.0}%", detail_used_pct),
                remaining: Some(remaining_pct),
                description: String::new(),
                next_reset_time: item_reset_dt,
            });

            min_remaining = min_remaining.min(remaining_pct);
        }

        // Sort: keep [Credits] at top, then alphabetical
        details.sort_by(|a, b| {
            let a_is_credits = a.name.starts_with("[Credits]");
            let b_is_credits = b.name.starts_with("[Credits]");

            match (a_is_credits, b_is_credits) {
                (true, false) => std::cmp::Ordering::Less,
                (false, true) => std::cmp::Ordering::Greater,
                _ => a.name.cmp(&b.name),
            }
        });

        // Calculate used percentage for consistency with other providers
        let used_pct_total = 100.0 - min_remaining;

        Ok(ProviderUsage {
            provider_id: "antigravity".to_string(),
            provider_name: "Antigravity".to_string(),
            usage_percentage: used_pct_total,
            remaining_percentage: Some(min_remaining),
            cost_used: used_pct_total,
            cost_limit: 100.0,
            usage_unit: "Quota %".to_string(),
            is_quota_based: true,
            payment_type: PaymentType::Quota,
            description: format!("{:.1}% Used", used_pct_total),
            details: if details.is_empty() {
                None
            } else {
                Some(details)
            },
            account_name: email,
            ..Default::default()
        })
    }
}

#[async_trait]
impl ProviderService for AntigravityProvider {
    fn provider_id(&self) -> &'static str {
        "antigravity"
    }

    async fn get_usage(&self, _config: &ProviderConfig) -> Vec<ProviderUsage> {
        #[cfg(not(windows))]
        {
            return vec![ProviderUsage {
                provider_id: self.provider_id().to_string(),
                provider_name: "Antigravity".to_string(),
                is_available: false,
                description: "Antigravity is only available on Windows".to_string(),
                ..Default::default()
            }];
        }

        #[cfg(windows)]
        {
            let mut results = Vec::new();

            // Find all Antigravity processes
            info!("Antigravity: Searching for running processes...");
            let process_infos = self.find_process_infos();
            info!("Antigravity: Found {} process(es)", process_infos.len());

            if process_infos.is_empty() {
                warn!("Antigravity: No processes found - extension may not be running");
                return vec![ProviderUsage {
                    provider_id: self.provider_id().to_string(),
                    provider_name: "Antigravity".to_string(),
                    is_available: false,
                    description: "Antigravity process not running".to_string(),
                    is_quota_based: true,
                    payment_type: PaymentType::Quota,
                    ..Default::default()
                }];
            }

            for (pid, csrf_token) in process_infos {
                debug!(
                    "Checking Antigravity process: PID={}, CSRF={}...",
                    pid,
                    &csrf_token[..8.min(csrf_token.len())]
                );

                // Find listening port
                let port = match self.find_listening_port(pid) {
                    Some(port) => port,
                    None => {
                        warn!("No listening port found for PID {}", pid);
                        continue;
                    }
                };

                // Fetch usage
                match self.fetch_usage(port, &csrf_token).await {
                    Ok(usage) => {
                        // Check for duplicates based on account name
                        if !results
                            .iter()
                            .any(|r: &ProviderUsage| r.account_name == usage.account_name)
                        {
                            results.push(usage);
                        }
                    }
                    Err(e) => {
                        warn!("Failed to fetch usage for PID {}: {}", pid, e);
                    }
                }
            }

            if results.is_empty() {
                vec![ProviderUsage {
                    provider_id: self.provider_id().to_string(),
                    provider_name: "Antigravity".to_string(),
                    is_available: false,
                    description: "Antigravity process not running or unreachable".to_string(),
                    is_quota_based: true,
                    payment_type: PaymentType::Quota,
                    ..Default::default()
                }]
            } else {
                results
            }
        }
    }
}

// Response structures
#[derive(Debug, Deserialize)]
struct AntigravityResponse {
    #[serde(rename = "userStatus")]
    user_status: Option<UserStatus>,
}

#[derive(Debug, Deserialize)]
struct UserStatus {
    email: Option<String>,
    #[serde(rename = "cascadeModelConfigData")]
    cascade_model_config_data: Option<CascadeModelConfigData>,
}

#[derive(Debug, Deserialize)]
struct CascadeModelConfigData {
    #[serde(rename = "clientModelConfigs")]
    client_model_configs: Option<Vec<ClientModelConfig>>,
    #[serde(rename = "clientModelSorts")]
    client_model_sorts: Option<Vec<ClientModelSort>>,
}

#[derive(Debug, Deserialize, Clone)]
struct ClientModelConfig {
    label: Option<String>,
    #[serde(rename = "quotaInfo")]
    quota_info: Option<QuotaInfo>,
}

#[derive(Debug, Deserialize, Clone)]
struct QuotaInfo {
    #[serde(rename = "remainingFraction")]
    remaining_fraction: Option<f64>,
    #[serde(rename = "totalRequests")]
    total_requests: Option<i32>,
    #[serde(rename = "usedRequests")]
    used_requests: Option<i32>,
    #[serde(rename = "resetTime")]
    reset_time: Option<String>,
}

#[derive(Debug, Deserialize, Clone)]
struct ClientModelSort {
    groups: Option<Vec<ModelGroup>>,
}

#[derive(Debug, Deserialize, Clone)]
struct ModelGroup {
    #[serde(rename = "modelLabels")]
    model_labels: Option<Vec<String>>,
}
