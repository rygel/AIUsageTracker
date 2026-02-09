use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AgentConfig {
    pub refresh_interval_minutes: u64,
    pub auto_refresh_enabled: bool,
}

impl Default for AgentConfig {
    fn default() -> Self {
        Self {
            refresh_interval_minutes: 5,
            auto_refresh_enabled: true,
        }
    }
}
