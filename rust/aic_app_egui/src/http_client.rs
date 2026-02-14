use crate::models::{AgentInfo, UsageResponse};
use thiserror::Error;

#[derive(Error, Debug)]
pub enum ClientError {
    #[error("HTTP request failed: {0}")]
    RequestFailed(#[from] reqwest::Error),
    #[error("Failed to parse response: {0}")]
    ParseError(#[from] serde_json::Error),
    #[error("Agent not running on port {0}")]
    AgentNotFound(u16),
    #[error("Agent not responding")]
    AgentNotResponding,
}

pub fn get_agent_port() -> u16 {
    let port_file_path = std::env::current_dir()
        .map(|p| p.join(".agent_port"))
        .unwrap_or_default();

    if let Ok(content) = std::fs::read_to_string(&port_file_path) {
        if let Ok(port) = content.trim().parse() {
            return port;
        }
    }
    8080
}

#[derive(Clone)]
pub struct AgentClient {
    client: reqwest::Client,
    port: u16,
}

impl AgentClient {
    pub fn new(port: u16) -> Self {
        let port = if port == 0 { get_agent_port() } else { port };
        let client = reqwest::Client::builder()
            .timeout(Duration::from_secs(10))
            .build()
            .unwrap_or_else(|_| reqwest::Client::new());
        Self { client, port }
    }

    pub fn with_auto_discovery() -> Self {
        Self::new(0)
    }

    fn base_url(&self) -> String {
        format!("http://localhost:{}", self.port)
    }

    pub fn port(&self) -> u16 {
        self.port
    }

    pub async fn get_usage(&self) -> Result<Vec<crate::models::ProviderUsage>, ClientError> {
        let url = format!("{}/api/providers/usage", self.base_url());
        let response = self.client.get(&url).send().await?;
        
        if response.status() == reqwest::StatusCode::NOT_FOUND {
            return Err(ClientError::AgentNotFound(self.port));
        }
        
        let providers: Vec<crate::models::ProviderUsage> = response.json().await?;
        Ok(providers)
    }

    pub async fn get_agent_info(&self) -> Result<AgentInfo, ClientError> {
        let url = format!("{}/api/agent/info", self.base_url());
        let response = self.client.get(&url).send().await?;
        
        if response.status() == reqwest::StatusCode::NOT_FOUND {
            return Err(ClientError::AgentNotFound(self.port));
        }
        
        let info: AgentInfo = response.json().await?;
        Ok(info)
    }

    pub async fn refresh_usage(&self) -> Result<(), ClientError> {
        let url = format!("{}/api/providers/usage/refresh", self.base_url());
        self.client.post(&url).send().await?;
        Ok(())
    }

    pub async fn health_check(&self) -> Result<bool, ClientError> {
        let url = format!("{}/health", self.base_url());
        match self.client.get(&url).send().await {
            Ok(response) => Ok(response.status().is_success()),
            Err(_) => Ok(false),
        }
    }

    pub async fn check_agent_status(&self) -> Result<AgentStatus, ClientError> {
        let url = format!("{}/health", self.base_url());
        
        match self.client.get(&url).send().await {
            Ok(response) if response.status().is_success() => {
                Ok(AgentStatus {
                    is_running: true,
                    port: self.port,
                    message: "Agent Connected".to_string(),
                })
            }
            Ok(_) => Ok(AgentStatus {
                is_running: false,
                port: self.port,
                message: "Agent not responding".to_string(),
            }),
            Err(_) => Ok(AgentStatus {
                is_running: false,
                port: self.port,
                message: "Agent not running".to_string(),
            }),
        }
    }

    pub async fn get_providers(&self) -> Result<Vec<serde_json::Value>, ClientError> {
        let url = format!("{}/api/providers/discovered", self.base_url());
        let response = self.client.get(&url).send().await?;
        
        if response.status() == reqwest::StatusCode::NOT_FOUND {
            return Err(ClientError::AgentNotFound(self.port));
        }
        
        let providers: Vec<serde_json::Value> = response.json().await?;
        Ok(providers)
    }

    pub async fn trigger_discovery(&self) -> Result<(), ClientError> {
        let url = format!("{}/api/discover", self.base_url());
        self.client.post(&url).send().await?;
        Ok(())
    }

    pub async fn get_history(&self, limit: Option<u32>) -> Result<Vec<serde_json::Value>, ClientError> {
        let limit_str = limit.map(|l| l.to_string()).unwrap_or_else(|| "50".to_string());
        let url = format!("{}/api/history?limit={}", self.base_url(), limit_str);
        let response = self.client.get(&url).send().await?;
        
        if response.status() == reqwest::StatusCode::NOT_FOUND {
            return Err(ClientError::AgentNotFound(self.port));
        }
        
        let history: Vec<serde_json::Value> = response.json().await?;
        Ok(history)
    }

    pub async fn get_github_auth_status(&self) -> Result<GitHubAuthStatus, ClientError> {
        let url = format!("{}/api/auth/github/status", self.base_url());
        let response = self.client.get(&url).send().await?;
        
        if response.status() == reqwest::StatusCode::NOT_FOUND {
            return Err(ClientError::AgentNotFound(self.port));
        }
        
        let status: GitHubAuthStatus = response.json().await?;
        Ok(status)
    }

    pub async fn initiate_github_device_flow(&self) -> Result<DeviceFlowResponse, ClientError> {
        let url = format!("{}/api/auth/github/device", self.base_url());
        let response = self.client.post(&url).send().await?;
        
        if response.status() == reqwest::StatusCode::NOT_FOUND {
            return Err(ClientError::AgentNotFound(self.port));
        }
        
        let flow: DeviceFlowResponse = response.json().await?;
        Ok(flow)
    }

    pub async fn poll_github_token(&self) -> Result<GitHubPollResponse, ClientError> {
        let url = format!("{}/api/auth/github/poll", self.base_url());
        let response = self.client.post(&url).send().await?;
        
        if response.status() == reqwest::StatusCode::NOT_FOUND {
            return Err(ClientError::AgentNotFound(self.port));
        }
        
        let result: GitHubPollResponse = response.json().await?;
        Ok(result)
    }

    pub async fn logout_github(&self) -> Result<(), ClientError> {
        let url = format!("{}/api/auth/github/logout", self.base_url());
        self.client.post(&url).send().await?;
        Ok(())
    }

    pub async fn save_provider_config(&self, config: &crate::models::ProviderConfig) -> Result<(), ClientError> {
        let url = format!("{}/api/providers/{}", self.base_url(), config.provider_id);
        self.client.put(&url).json(config).send().await?;
        Ok(())
    }

    pub async fn get_raw_responses(&self, provider_id: Option<&str>, limit: Option<u32>) -> Result<Vec<serde_json::Value>, ClientError> {
        let mut url = format!("{}/api/raw_responses", self.base_url());
        let mut params = Vec::new();
        
        if let Some(pid) = provider_id {
            params.push(format!("provider_id={}", pid));
        }
        if let Some(l) = limit {
            params.push(format!("limit={}", l));
        }
        
        if !params.is_empty() {
            url.push('?');
            url.push_str(&params.join("&"));
        }
        
        let response = self.client.get(&url).send().await?;
        
        if response.status() == reqwest::StatusCode::NOT_FOUND {
            return Err(ClientError::AgentNotFound(self.port));
        }
        
        let logs: Vec<serde_json::Value> = response.json().await?;
        Ok(logs)
    }
}

#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
pub struct AgentStatus {
    pub is_running: bool,
    pub port: u16,
    pub message: String,
}

#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
pub struct GitHubAuthStatus {
    pub is_authenticated: bool,
    pub username: Option<String>,
    pub token_invalid: bool,
}

#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
pub struct DeviceFlowResponse {
    pub success: bool,
    pub user_code: Option<String>,
    pub verification_uri: Option<String>,
    pub interval: Option<u64>,
    pub expires_in: Option<u64>,
}

#[derive(Debug, Clone, serde::Serialize, serde::Deserialize)]
pub struct GitHubPollResponse {
    pub success: bool,
    pub status: String,
    pub username: Option<String>,
}

use std::time::Duration;
