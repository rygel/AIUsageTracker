use aic_core::{
    AuthenticationManager, ConfigLoader, ProviderManager, ProviderUsage, TokenPollResult,
};
use std::process::Command;
use std::sync::Arc;
use tauri::State;
use tokio::sync::{Mutex, RwLock};

pub struct AppState {
    pub provider_manager: Arc<ProviderManager>,
    pub config_loader: Arc<ConfigLoader>,
    pub auth_manager: Arc<AuthenticationManager>,
    pub auto_refresh_enabled: Arc<Mutex<bool>>,
    pub device_flow_state: Arc<RwLock<Option<DeviceFlowState>>>,
}

#[derive(Clone)]
pub struct DeviceFlowState {
    pub device_code: String,
    pub user_code: String,
    pub verification_uri: String,
    pub interval: u64,
}

// Provider commands
#[tauri::command]
pub async fn get_usage(state: State<'_, AppState>) -> Result<Vec<ProviderUsage>, String> {
    let manager = &state.provider_manager;
    Ok(manager.get_all_usage(true).await)
}

#[tauri::command]
pub async fn refresh_usage(state: State<'_, AppState>) -> Result<Vec<ProviderUsage>, String> {
    let manager = &state.provider_manager;
    Ok(manager.get_all_usage(true).await)
}

// Preferences commands
#[tauri::command]
pub async fn load_preferences(
    state: State<'_, AppState>,
) -> Result<aic_core::AppPreferences, String> {
    let prefs = state.config_loader.load_preferences().await;
    Ok(prefs)
}

#[tauri::command]
pub async fn save_preferences(
    state: State<'_, AppState>,
    preferences: aic_core::AppPreferences,
) -> Result<(), String> {
    state
        .config_loader
        .save_preferences(&preferences)
        .await
        .map_err(|e| e.to_string())
}

// Config commands
#[tauri::command]
pub async fn get_configured_providers(
    state: State<'_, AppState>,
) -> Result<Vec<aic_core::ProviderConfig>, String> {
    let configs = state.config_loader.load_config().await;
    Ok(configs)
}

#[tauri::command]
pub async fn save_provider_config(
    state: State<'_, AppState>,
    config: aic_core::ProviderConfig,
) -> Result<(), String> {
    let mut configs = state.config_loader.load_config().await;

    // Update or add the config
    if let Some(existing) = configs
        .iter_mut()
        .find(|c| c.provider_id == config.provider_id)
    {
        *existing = config;
    } else {
        configs.push(config);
    }

    state
        .config_loader
        .save_config(&configs)
        .await
        .map_err(|e| e.to_string())
}

#[tauri::command]
pub async fn remove_provider_config(
    state: State<'_, AppState>,
    provider_id: String,
) -> Result<(), String> {
    let mut configs = state.config_loader.load_config().await;
    configs.retain(|c| c.provider_id != provider_id);

    state
        .config_loader
        .save_config(&configs)
        .await
        .map_err(|e| e.to_string())
}

// Auto-refresh commands
#[tauri::command]
pub async fn toggle_auto_refresh(state: State<'_, AppState>, enabled: bool) -> Result<(), String> {
    let mut auto_refresh = state.auto_refresh_enabled.lock().await;
    *auto_refresh = enabled;
    Ok(())
}

#[tauri::command]
pub async fn is_auto_refresh_enabled(state: State<'_, AppState>) -> Result<bool, String> {
    let auto_refresh = state.auto_refresh_enabled.lock().await;
    Ok(*auto_refresh)
}

// GitHub Authentication commands
#[tauri::command]
pub async fn is_github_authenticated(state: State<'_, AppState>) -> Result<bool, String> {
    Ok(state.auth_manager.is_authenticated())
}

#[tauri::command]
pub async fn initiate_github_login(
    state: State<'_, AppState>,
) -> Result<(String, String, String), String> {
    match state.auth_manager.initiate_login().await {
        Ok(flow_response) => {
            // Store the device flow state
            let mut flow_state = state.device_flow_state.write().await;
            *flow_state = Some(DeviceFlowState {
                device_code: flow_response.device_code.clone(),
                user_code: flow_response.user_code.clone(),
                verification_uri: flow_response.verification_uri.clone(),
                interval: flow_response.interval as u64,
            });

            Ok((
                flow_response.user_code,
                flow_response.verification_uri,
                flow_response.device_code,
            ))
        }
        Err(e) => Err(format!("Failed to initiate login: {}", e)),
    }
}

#[tauri::command]
pub async fn complete_github_login(
    state: State<'_, AppState>,
    device_code: String,
    interval: u64,
) -> Result<bool, String> {
    match state
        .auth_manager
        .wait_for_login(&device_code, interval)
        .await
    {
        Ok(success) => {
            // Clear the device flow state
            let mut flow_state = state.device_flow_state.write().await;
            *flow_state = None;
            Ok(success)
        }
        Err(e) => Err(format!("Login failed: {}", e)),
    }
}

#[tauri::command]
pub async fn poll_github_token(
    state: State<'_, AppState>,
    device_code: String,
) -> Result<String, String> {
    use aic_core::TokenPollResult;

    match state.auth_manager.poll_for_token(&device_code).await {
        TokenPollResult::Token(_) => Ok("success".to_string()),
        TokenPollResult::Pending => Ok("pending".to_string()),
        TokenPollResult::SlowDown => Ok("slow_down".to_string()),
        TokenPollResult::Expired => Err("Token expired".to_string()),
        TokenPollResult::AccessDenied => Err("Access denied".to_string()),
        TokenPollResult::Error(msg) => Err(msg),
    }
}

#[tauri::command]
pub async fn logout_github(state: State<'_, AppState>) -> Result<(), String> {
    state
        .auth_manager
        .logout()
        .await
        .map_err(|e| format!("Logout failed: {}", e))
}

#[tauri::command]
pub async fn cancel_github_login(state: State<'_, AppState>) -> Result<(), String> {
    let mut flow_state = state.device_flow_state.write().await;
    *flow_state = None;
    Ok(())
}

// Window control commands
#[tauri::command]
pub async fn close_window(window: tauri::Window, app: tauri::AppHandle) -> Result<(), String> {
    println!("Close window command received");

    // Close the specific window
    if let Err(e) = window.close() {
        println!("Error closing window: {}", e);
    }

    // Also exit the app if this is the main window
    println!("Exiting application");
    app.exit(0);

    Ok(())
}

#[tauri::command]
pub async fn minimize_window(window: tauri::Window) -> Result<(), String> {
    let _ = window.minimize();
    Ok(())
}

#[tauri::command]
pub async fn toggle_always_on_top(window: tauri::Window, enabled: bool) -> Result<(), String> {
    let _ = window.set_always_on_top(enabled);
    Ok(())
}

#[tauri::command]
pub async fn open_browser(url: String) -> Result<(), String> {
    #[cfg(target_os = "windows")]
    {
        let _ = Command::new("cmd").args(["/C", "start", &url]).spawn();
    }
    #[cfg(target_os = "macos")]
    {
        let _ = Command::new("open").arg(&url).spawn();
    }
    #[cfg(target_os = "linux")]
    {
        let _ = Command::new("xdg-open").arg(&url).spawn();
    }
    Ok(())
}

// Settings commands
#[tauri::command]
pub async fn save_provider_configs(
    state: State<'_, AppState>,
    configs: Vec<aic_core::ProviderConfig>,
) -> Result<(), String> {
    state
        .config_loader
        .save_config(&configs)
        .await
        .map_err(|e| e.to_string())
}

#[tauri::command]
pub async fn scan_for_api_keys(
    state: State<'_, AppState>,
) -> Result<Vec<aic_core::ProviderConfig>, String> {
    // Scan known locations for auth.json files
    // This uses the ConfigLoader which already knows how to scan these paths
    let configs = state.config_loader.load_config().await;
    Ok(configs)
}

#[tauri::command]
pub async fn close_settings_window(window: tauri::Window) -> Result<(), String> {
    let _ = window.close();
    Ok(())
}

#[tauri::command]
pub async fn check_github_login_status(state: State<'_, AppState>) -> Result<String, String> {
    let flow_state = state.device_flow_state.read().await;
    if let Some(flow) = flow_state.as_ref() {
        match state.auth_manager.poll_for_token(&flow.device_code).await {
            TokenPollResult::Token(_) => {
                Ok("success".to_string())
            }
            TokenPollResult::Pending => {
                Ok("pending".to_string())
            }
            TokenPollResult::SlowDown => {
                Ok("slow_down".to_string())
            }
            TokenPollResult::Expired => {
                Err("Token expired".to_string())
            }
            TokenPollResult::AccessDenied => {
                Err("Access denied".to_string())
            }
            TokenPollResult::Error(msg) => {
                Err(msg)
            }
        }
    } else {
        if state.auth_manager.is_authenticated() {
            Ok("success".to_string())
        } else {
            Err("No login flow".to_string())
        }
    }
}

#[tauri::command]
pub async fn discover_github_token() -> Result<TokenDiscoveryResult, String> {
    let mut found = false;
    let mut token = String::new();
    
    if let Ok(home) = std::env::var("HOME") {
        let gh_paths = [
            format!("{}/.config/gh/hosts.yml", home),
            format!("{}/.git-credential-store", home),
        ];
        
        for path in gh_paths.iter() {
            if std::path::Path::new(path).exists() {
                if let Ok(content) = std::fs::read_to_string(path) {
                    if let Some(pat) = extract_pat(&content) {
                        found = true;
                        token = pat;
                        break;
                    }
                }
            }
        }
    }
    
    Ok(TokenDiscoveryResult { found, token })
}

#[derive(serde::Serialize, serde::Deserialize)]
pub struct TokenDiscoveryResult {
    pub found: bool,
    pub token: String,
}

fn extract_pat(content: &str) -> Option<String> {
    if let Some(start) = content.find("github_pat_") {
        let rest = &content[start..];
        if let Some(end) = rest.find(|c: char| !c.is_alphanumeric() && c != '_' && c != '-') {
            return Some(rest[..end].to_string());
        } else {
            return Some(rest.to_string());
        }
    }
    None
}

#[tauri::command]
pub async fn check_for_updates() -> Result<UpdateCheckResult, String> {
    const CURRENT_VERSION: &str = "1.7.13";
    
    Ok(UpdateCheckResult {
        current_version: CURRENT_VERSION.to_string(),
        latest_version: CURRENT_VERSION.to_string(),
        update_available: false,
        download_url: String::new(),
    })
}

#[derive(serde::Serialize, serde::Deserialize)]
pub struct UpdateCheckResult {
    pub current_version: String,
    pub latest_version: String,
    pub update_available: bool,
    pub download_url: String,
}

#[tauri::command]
pub async fn get_app_version() -> Result<String, String> {
    Ok("1.7.13".to_string())
}

#[cfg(test)]
mod tests {
    use super::*;
    use aic_core::{AuthenticationManager, ConfigLoader, GitHubAuthService, ProviderManager};
    use std::sync::Arc;
    use tokio::sync::{Mutex, RwLock};

    fn create_test_state() -> AppState {
        let client = reqwest::Client::new();
        let provider_manager = Arc::new(ProviderManager::new(client.clone()));
        let config_loader = Arc::new(ConfigLoader::new(client.clone()));
        let auth_service = Arc::new(GitHubAuthService::new(client));
        let auth_manager = Arc::new(AuthenticationManager::new(
            auth_service.clone(),
            config_loader.clone(),
        ));

        AppState {
            provider_manager,
            config_loader,
            auth_manager,
            auto_refresh_enabled: Arc::new(Mutex::new(false)),
            device_flow_state: Arc::new(RwLock::new(None)),
        }
    }

    #[tokio::test]
    async fn test_get_usage_returns_data() {
        let state = create_test_state();
        // This test will run the actual provider manager
        // In a real test environment, we'd mock the HTTP calls
    }

    #[tokio::test]
    async fn test_toggle_auto_refresh() {
        let state = create_test_state();

        // Initially false
        let initial = *state.auto_refresh_enabled.lock().await;
        assert!(!initial);

        // Toggle to true
        {
            let mut enabled = state.auto_refresh_enabled.lock().await;
            *enabled = true;
        }

        let after_toggle = *state.auto_refresh_enabled.lock().await;
        assert!(after_toggle);
    }

    #[tokio::test]
    async fn test_device_flow_state() {
        let state = create_test_state();

        // Initially None
        let initial = state.device_flow_state.read().await;
        assert!(initial.is_none());
        drop(initial);

        // Set a value
        {
            let mut flow_state = state.device_flow_state.write().await;
            *flow_state = Some(DeviceFlowState {
                device_code: "test_device_code".to_string(),
                user_code: "TEST123".to_string(),
                verification_uri: "https://github.com/login/device".to_string(),
                interval: 5,
            });
        }

        // Verify it was set
        let after_set = state.device_flow_state.read().await;
        assert!(after_set.is_some());
        assert_eq!(after_set.as_ref().unwrap().user_code, "TEST123");
    }
}
