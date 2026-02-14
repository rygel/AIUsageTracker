use log::{error, info};
use reqwest::Client;
use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::sync::{Arc, Mutex};

/// GitHub OAuth2 Device Flow authentication service
pub struct GitHubAuthService {
    client: Client,
    current_token: Arc<Mutex<Option<String>>>,
}

// Using VS Code's Client ID for Copilot integrations
const CLIENT_ID: &str = "Iv1.b507a08c87ecfe98";
const AUTH_URL: &str = "https://github.com/login/device/code";
const TOKEN_URL: &str = "https://github.com/login/oauth/access_token";
const SCOPE: &str = "read:user copilot";

/// Response from initiating device flow
#[derive(Debug, Clone)]
pub struct DeviceFlowResponse {
    pub device_code: String,
    pub user_code: String,
    pub verification_uri: String,
    pub expires_in: i64,
    pub interval: i64,
}

/// Token polling result
#[derive(Debug, Clone)]
pub enum TokenPollResult {
    /// Token received successfully
    Token(String),
    /// Authorization still pending, continue polling
    Pending,
    /// Need to slow down polling
    SlowDown,
    /// Token expired
    Expired,
    /// Access denied by user
    AccessDenied,
    /// Unknown error
    Error(String),
}

impl GitHubAuthService {
    pub fn new(client: Client) -> Self {
        Self {
            client,
            current_token: Arc::new(Mutex::new(None)),
        }
    }

    /// Check if currently authenticated
    pub fn is_authenticated(&self) -> bool {
        self.current_token
            .lock()
            .map(|token| token.is_some())
            .unwrap_or(false)
    }

    /// Get the current token if authenticated
    pub fn get_current_token(&self) -> Option<String> {
        self.current_token.lock().ok()?.clone()
    }

    /// Initialize with an existing token
    pub fn initialize_token(&self, token: String) {
        if let Ok(mut current) = self.current_token.lock() {
            *current = Some(token);
            info!("GitHub token initialized");
        }
    }

    /// Logout and clear the token
    pub fn logout(&self) {
        if let Ok(mut current) = self.current_token.lock() {
            *current = None;
            info!("GitHub token cleared");
        }
    }

    /// Get the username of the authenticated user
    pub async fn get_username(&self) -> Option<String> {
        let token = self.get_current_token()?;
        let response = self
            .client
            .get("https://api.github.com/user")
            .header("Authorization", format!("Bearer {}", token))
            .header("User-Agent", "AIConsumptionTracker/1.0")
            .send()
            .await
            .ok()?;

        if response.status().is_success() {
            let json: serde_json::Value = response.json().await.ok()?;
            json.get("login").and_then(|v| v.as_str()).map(|s| s.to_string())
        } else {
            None
        }
    }

    /// Initiate the OAuth2 Device Flow
    /// Returns device code, user code, verification URI, and polling parameters
    pub async fn initiate_device_flow(&self) -> Result<DeviceFlowResponse, String> {
        let mut params = HashMap::new();
        params.insert("client_id", CLIENT_ID);
        params.insert("scope", SCOPE);

        let response = self
            .client
            .post(AUTH_URL)
            .header("Accept", "application/json")
            .form(&params)
            .send()
            .await
            .map_err(|e| format!("Request failed: {}", e))?;

        if !response.status().is_success() {
            return Err(format!(
                "Failed to initiate device flow: {}",
                response.status()
            ));
        }

        let response_data: DeviceFlowInitResponse = response
            .json()
            .await
            .map_err(|e| format!("Failed to parse response: {}", e))?;

        info!(
            "Device flow initiated. User code: {}",
            response_data.user_code
        );

        Ok(DeviceFlowResponse {
            device_code: response_data.device_code,
            user_code: response_data.user_code,
            verification_uri: response_data.verification_uri,
            expires_in: response_data.expires_in,
            interval: response_data.interval,
        })
    }

    /// Poll for the access token (single check)
    /// Callers should loop with appropriate delays based on interval
    pub async fn poll_for_token(&self, device_code: &str) -> TokenPollResult {
        let mut params = HashMap::new();
        params.insert("client_id", CLIENT_ID);
        params.insert("device_code", device_code);
        params.insert("grant_type", "urn:ietf:params:oauth:grant-type:device_code");

        match self
            .client
            .post(TOKEN_URL)
            .header("Accept", "application/json")
            .form(&params)
            .send()
            .await
        {
            Ok(response) => {
                if !response.status().is_success() {
                    return TokenPollResult::Error(format!("HTTP error: {}", response.status()));
                }

                match response.json::<serde_json::Value>().await {
                    Ok(json) => {
                        // Check for errors
                        if let Some(error) = json.get("error").and_then(|e| e.as_str()) {
                            match error {
                                "authorization_pending" => TokenPollResult::Pending,
                                "slow_down" => TokenPollResult::SlowDown,
                                "expired_token" => TokenPollResult::Expired,
                                "access_denied" => TokenPollResult::AccessDenied,
                                _ => TokenPollResult::Error(format!("Unknown error: {}", error)),
                            }
                        } else if let Some(token) =
                            json.get("access_token").and_then(|t| t.as_str())
                        {
                            // Success! Store the token
                            let token = token.to_string();
                            if let Ok(mut current) = self.current_token.lock() {
                                *current = Some(token.clone());
                            }
                            info!("GitHub token received successfully");
                            TokenPollResult::Token(token)
                        } else {
                            TokenPollResult::Error("No access_token in response".to_string())
                        }
                    }
                    Err(e) => {
                        error!("Failed to parse token response: {}", e);
                        TokenPollResult::Error(format!("Parse error: {}", e))
                    }
                }
            }
            Err(e) => {
                error!("Failed to poll for token: {}", e);
                TokenPollResult::Error(format!("Request error: {}", e))
            }
        }
    }

    /// Complete device flow with automatic polling
    /// Polls until success, expiration, or denial
    pub async fn complete_device_flow(
        &self,
        device_code: &str,
        interval: u64,
        max_attempts: Option<u32>,
    ) -> Result<String, String> {
        let max_attempts = max_attempts.unwrap_or(300); // Default 5 minutes at 1 second intervals
        let mut attempts = 0;

        loop {
            if attempts >= max_attempts {
                return Err("Max polling attempts reached".to_string());
            }
            attempts += 1;

            match self.poll_for_token(device_code).await {
                TokenPollResult::Token(token) => return Ok(token),
                TokenPollResult::Pending => {
                    // Wait for the specified interval
                    tokio::time::sleep(tokio::time::Duration::from_secs(interval)).await;
                }
                TokenPollResult::SlowDown => {
                    // Slow down by doubling the interval
                    tokio::time::sleep(tokio::time::Duration::from_secs(interval * 2)).await;
                }
                TokenPollResult::Expired => return Err("Token expired".to_string()),
                TokenPollResult::AccessDenied => return Err("Access denied by user".to_string()),
                TokenPollResult::Error(msg) => return Err(msg),
            }
        }
    }
}

/// Response from device flow initiation
#[derive(Debug, Deserialize)]
struct DeviceFlowInitResponse {
    device_code: String,
    user_code: String,
    verification_uri: String,
    expires_in: i64,
    interval: i64,
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_is_authenticated_initially_false() {
        let service = GitHubAuthService::new(Client::new());
        assert!(!service.is_authenticated());
    }

    #[test]
    fn test_initialize_token() {
        let service = GitHubAuthService::new(Client::new());
        service.initialize_token("test_token".to_string());

        assert!(service.is_authenticated());
        assert_eq!(service.get_current_token(), Some("test_token".to_string()));
    }

    #[test]
    fn test_logout() {
        let service = GitHubAuthService::new(Client::new());
        service.initialize_token("test_token".to_string());

        service.logout();

        assert!(!service.is_authenticated());
        assert_eq!(service.get_current_token(), None);
    }
}
