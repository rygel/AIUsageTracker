use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct UsageResponse {
    pub version: String,
    pub providers: Vec<ProviderUsage>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub prefs: Option<AppPreferences>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ProviderUsage {
    #[serde(rename = "provider_id")]
    pub provider_id: String,
    #[serde(rename = "provider_name")]
    pub provider_name: String,
    #[serde(rename = "usage_percentage")]
    pub usage_percentage: f64,
    #[serde(rename = "remaining_percentage")]
    #[serde(skip_serializing_if = "Option::is_none")]
    pub remaining_percentage: Option<f64>,
    #[serde(rename = "cost_used")]
    pub cost_used: f64,
    #[serde(rename = "cost_limit")]
    pub cost_limit: f64,
    #[serde(rename = "payment_type")]
    pub payment_type: String,
    #[serde(rename = "usage_unit")]
    pub usage_unit: String,
    #[serde(rename = "is_quota_based")]
    pub is_quota_based: bool,
    #[serde(rename = "is_available")]
    pub is_available: bool,
    pub description: String,
    #[serde(rename = "auth_source")]
    pub auth_source: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub details: Option<Vec<ProviderUsageDetail>>,
    #[serde(rename = "account_name")]
    pub account_name: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    #[serde(rename = "next_reset_time")]
    pub next_reset_time: Option<DateTime<Utc>>,
    #[serde(skip_serializing_if = "Option::is_none")]
    #[serde(rename = "raw_response")]
    pub raw_response: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ProviderUsageDetail {
    pub name: String,
    pub used: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub remaining: Option<f64>,
    pub description: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub next_reset_time: Option<DateTime<Utc>>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AppPreferences {
    #[serde(rename = "show_all")]
    pub show_all: bool,
    #[serde(rename = "window_width")]
    pub window_width: f64,
    #[serde(rename = "window_height")]
    pub window_height: f64,
    #[serde(rename = "stay_open")]
    pub stay_open: bool,
    #[serde(rename = "always_on_top")]
    pub always_on_top: bool,
    #[serde(rename = "compact_mode")]
    pub compact_mode: bool,
    #[serde(rename = "color_threshold_yellow")]
    pub color_threshold_yellow: i32,
    #[serde(rename = "color_threshold_red")]
    pub color_threshold_red: i32,
    #[serde(rename = "invert_progress_bar")]
    pub invert_progress_bar: bool,
    #[serde(rename = "font_family")]
    pub font_family: String,
    #[serde(rename = "font_size")]
    pub font_size: i32,
    #[serde(rename = "font_bold")]
    pub font_bold: bool,
    #[serde(rename = "font_italic")]
    pub font_italic: bool,
}

impl Default for AppPreferences {
    fn default() -> Self {
        Self {
            show_all: false,
            window_width: 420.0,
            window_height: 500.0,
            stay_open: false,
            always_on_top: true,
            compact_mode: true,
            color_threshold_yellow: 60,
            color_threshold_red: 80,
            invert_progress_bar: false,
            font_family: "Segoe UI".to_string(),
            font_size: 12,
            font_bold: false,
            font_italic: false,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ProviderConfig {
    #[serde(rename = "provider_id")]
    pub provider_id: String,
    #[serde(rename = "api_key")]
    pub api_key: String,
    #[serde(rename = "type")]
    pub config_type: String,
    #[serde(rename = "payment_type")]
    pub payment_type: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub limit: Option<f64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    #[serde(rename = "base_url")]
    pub base_url: Option<String>,
    #[serde(rename = "show_in_tray")]
    pub show_in_tray: bool,
    #[serde(rename = "enabled_sub_trays")]
    pub enabled_sub_trays: Vec<String>,
    #[serde(rename = "auth_source")]
    pub auth_source: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AgentInfo {
    pub version: String,
    #[serde(rename = "agent_path")]
    pub agent_path: String,
    #[serde(rename = "uptime_seconds")]
    pub uptime_seconds: u64,
    #[serde(rename = "database_path")]
    pub database_path: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct HistoryEntry {
    pub id: i64,
    pub timestamp: DateTime<Utc>,
    pub provider_id: String,
    pub provider_name: String,
    pub cost_used: f64,
    pub requests_count: i64,
    pub tokens_input: i64,
    pub tokens_output: i64,
}
