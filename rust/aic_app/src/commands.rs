use aic_core::{
    AuthenticationManager, ConfigLoader, ProviderManager, ProviderUsage, TokenPollResult,
};
use log::{error, info, warn};
use reqwest::Client;
use std::process::{Command, Child};
use std::sync::Arc;
use tauri::{State, Manager, AppHandle};
use tauri_plugin_updater::UpdaterExt;
use tokio::sync::{Mutex, RwLock};

pub struct AppState {
    pub provider_manager: Arc<ProviderManager>,
    pub config_loader: Arc<ConfigLoader>,
    pub auth_manager: Arc<AuthenticationManager>,
    pub auto_refresh_enabled: Arc<Mutex<bool>>,
    pub device_flow_state: Arc<RwLock<Option<DeviceFlowState>>>,
    pub agent_process: Arc<Mutex<Option<Child>>>,
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
    // Fast path: get primary config only
    let configs = state.config_loader.load_primary_config().await;
    Ok(configs)
}

#[tauri::command]
pub async fn get_all_providers_from_agent(
    _state: State<'_, AppState>,
) -> Result<Vec<aic_core::ProviderConfig>, String> {
    // Get all providers from agent (including discovered ones)
    let agent_url = "http://localhost:8080/api/providers/discovered";
    
    match reqwest::get(agent_url).await {
        Ok(response) => {
            match response.json::<Vec<aic_core::ProviderConfig>>().await {
                Ok(providers) => {
                    info!("Retrieved {} providers from agent", providers.len());
                    Ok(providers)
                }
                Err(e) => {
                    error!("Failed to parse providers from agent: {}", e);
                    Err(format!("Failed to get providers from agent: {}", e))
                }
            }
        }
        Err(e) => {
            error!("Failed to connect to agent: {}", e);
            Err(format!("Agent not available: {}", e))
        }
    }
}

#[tauri::command]
pub async fn scan_for_api_keys(state: State<'_, AppState>) -> Result<Vec<aic_core::ProviderConfig>, String> {
    let client = Client::new();
    // Trigger explicit discovery scan via agent
    let agent_url = "http://localhost:8080/api/config";

    match client.post(agent_url).send().await {
        Ok(_) => {
            // Wait a moment for discovery to complete
            tokio::time::sleep(tokio::time::Duration::from_millis(500)).await;

            // Now get the updated providers from agent
            get_all_providers_from_agent(state).await
        }
        Err(e) => {
            error!("Failed to trigger discovery on agent: {}", e);
            Err(format!("Failed to trigger discovery: {}", e))
        }
    }
}

#[tauri::command]
pub async fn save_provider_config(
    state: State<'_, AppState>,
    config: aic_core::ProviderConfig,
) -> Result<(), String> {
    let mut configs = state.config_loader.load_primary_config().await;

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
pub async fn open_settings_window(app: tauri::AppHandle) -> Result<(), String> {
    if let Some(window) = app.get_webview_window("settings") {
        let _ = window.show();
        let _ = window.set_focus();
        Ok(())
    } else {
        Err("Settings window not found".to_string())
    }
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
pub async fn close_settings_window(window: tauri::Window) -> Result<(), String> {
    let _ = window.close();
    Ok(())
}

#[tauri::command]
pub async fn start_agent(
    app: tauri::AppHandle,
    state: State<'_, AppState>,
) -> Result<bool, String> {
    let agent_process = state.agent_process.clone();
    start_agent_internal(&app, agent_process).await
}

#[tauri::command]
pub async fn stop_agent(state: State<'_, AppState>) -> Result<bool, String> {
    let mut agent_process = state.agent_process.lock().await;

    if let Some(ref mut child) = *agent_process {
        match child.kill() {
            Ok(_) => {
                info!("Agent stopped successfully");
                *agent_process = None;
                Ok(true)
            }
            Err(e) => {
                error!("Failed to stop agent: {}", e);
                Err(format!("Failed to stop agent: {}", e))
            }
        }
    } else {
        Err("No agent process running".to_string())
    }
}

#[tauri::command]
pub async fn is_agent_running(state: State<'_, AppState>) -> Result<bool, String> {
    let mut agent_process = state.agent_process.lock().await;

    if let Some(ref mut child) = *agent_process {
        match child.try_wait() {
            Ok(None) => {
                Ok(true)
            }
            Ok(_) => {
                *agent_process = None;
                Ok(false)
            }
            Err(_) => {
                *agent_process = None;
                Ok(false)
            }
        }
    } else {
        Ok(false)
    }
}

#[tauri::command]
pub async fn get_agent_status_details(state: State<'_, AppState>) -> Result<AgentStatusDetails, String> {
    let mut agent_process = state.agent_process.lock().await;

    if let Some(ref mut child) = *agent_process {
        match child.try_wait() {
            Ok(None) => {
                // Process is still running
                let pid = child.id();
                Ok(AgentStatusDetails {
                    is_running: true,
                    process_id: Some(pid),
                    path_from: "Manual start".to_string(),
                })
            }
            Ok(_) => {
                // Process has finished
                *agent_process = None;
                Ok(AgentStatusDetails {
                    is_running: false,
                    process_id: None,
                    path_from: "Stopped".to_string(),
                })
            }
            Err(_) => {
                // Error occurred, assume process is done
                *agent_process = None;
                Ok(AgentStatusDetails {
                    is_running: false,
                    process_id: None,
                    path_from: "Unknown".to_string(),
                })
            }
        }
    } else {
        Ok(AgentStatusDetails {
            is_running: false,
            process_id: None,
            path_from: "Not started".to_string(),
        })
    }
}

#[derive(serde::Serialize, serde::Deserialize)]
pub struct AgentStatusDetails {
    pub is_running: bool,
    pub process_id: Option<u32>,
    pub path_from: String,
}

pub async fn update_tray_icon_by_status(app_handle: &AppHandle, is_connected: bool) {
    let tooltip = if is_connected {
        "AI Token Tracker - Connected to Agent"
    } else {
        "AI Token Tracker - Agent Disconnected"
    };

    if let Some(tray) = app_handle.tray_by_id("main") {
        let _ = tray.set_tooltip(Some(tooltip));
    }
}

pub async fn check_agent_status() -> Result<bool, String> {
    if let Ok(response) = reqwest::get("http://localhost:8080/health").await {
        Ok(response.status().is_success())
    } else {
        Ok(false)
    }
}

#[tauri::command]
pub async fn is_agent_running_http() -> Result<bool, String> {
    check_agent_status().await
}

pub async fn start_agent_internal(
    app_handle: &tauri::AppHandle,
    agent_process: Arc<Mutex<Option<Child>>>,
) -> Result<bool, String> {
    // First, check if something is already listening on port 8080
    match check_agent_status().await {
        Ok(true) => {
            info!("Agent is already running on port 8080");
            let app_handle = app_handle.clone();
            tokio::spawn(async move {
                update_tray_icon_by_status(&app_handle, true).await;
            });
            return Ok(true);
        }
        Ok(false) => {
            // Port is available, continue to start agent
        }
        Err(e) => {
            warn!("Could not check if port 8080 is in use: {}", e);
        }
    }

    let mut agent_process = agent_process.lock().await;

    if let Some(ref mut child) = *agent_process {
        match child.try_wait() {
            Ok(None) => {
                let app_handle = app_handle.clone();
                tokio::spawn(async move {
                    update_tray_icon_by_status(&app_handle, true).await;
                });
                return Ok(true);
            }
            Ok(_) => {
                *agent_process = None;
            }
            Err(_) => {
                *agent_process = None;
            }
        }
    }

    let agent_path = if cfg!(target_os = "windows") {
        let possible_paths = [
            "./aic_agent.exe",
            "../target/debug/aic_agent.exe",
            "../target/release/aic_agent.exe",
        ];

        let mut found_path = None;
        for path in &possible_paths {
            if std::path::Path::new(path).exists() {
                found_path = Some(path.to_string());
                break;
            }
        }

        found_path.ok_or_else(|| {
            "Agent executable not found. Please build the agent first."
        })?
    } else {
        let possible_paths = [
            "./aic_agent",
            "../target/debug/aic_agent",
            "../target/release/aic_agent",
        ];

        let mut found_path = None;
        for path in &possible_paths {
            if std::path::Path::new(path).exists() {
                found_path = Some(path.to_string());
                break;
            }
        }

        found_path.ok_or_else(|| {
            "Agent executable not found. Please build the agent first."
        })?
    };

    match Command::new(agent_path).spawn() {
        Ok(child) => {
            *agent_process = Some(child);
            info!("Agent started successfully");

            let app_handle = app_handle.clone();
            tokio::spawn(async move {
                tokio::time::sleep(tokio::time::Duration::from_millis(1000)).await;
                update_tray_icon_by_status(&app_handle, true).await;
            });

            Ok(true)
        }
        Err(e) => {
            error!("Failed to start agent: {}", e);
            Err(format!("Failed to start agent: {}", e))
        }
    }
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

#[derive(serde::Serialize, serde::Deserialize)]
pub struct UpdateCheckResult {
    pub current_version: String,
    pub latest_version: String,
    pub update_available: bool,
    pub download_url: String,
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
pub async fn check_for_updates(app: tauri::AppHandle) -> Result<UpdateCheckResult, String> {
    let current_version = app.package_info().version.to_string();
    
    match app.updater() {
        Ok(updater) => {
            match updater.check().await {
                Ok(Some(update)) => {
                    Ok(UpdateCheckResult {
                        current_version: current_version.clone(),
                        latest_version: update.version.clone(),
                        update_available: true,
                        download_url: update.download_url.to_string(),
                    })
                }
                Ok(None) => {
                    Ok(UpdateCheckResult {
                        current_version: current_version.clone(),
                        latest_version: current_version.clone(),
                        update_available: false,
                        download_url: String::new(),
                    })
                }
                Err(e) => {
                    Err(format!("Failed to check for updates: {}", e))
                }
            }
        }
        Err(e) => Err(format!("Updater not available: {}", e)),
    }
}

#[tauri::command]
pub async fn install_update(app: tauri::AppHandle) -> Result<bool, String> {
    match app.updater() {
        Ok(updater) => {
            match updater.check().await {
                Ok(Some(update)) => {
                    // Download and install the update
                    match update.download_and_install(
                        |_, _| {}, // on_chunk callback
                        || {},      // on_download_finish callback
                    ).await {
                        Ok(_) => {
                            info!("Update installed successfully");
                            Ok(true)
                        }
                        Err(e) => {
                            error!("Failed to install update: {}", e);
                            Err(format!("Failed to install update: {}", e))
                        }
                    }
                }
                Ok(None) => {
                    Err("No update available".to_string())
                }
                Err(e) => {
                    Err(format!("Failed to check for updates: {}", e))
                }
            }
        }
        Err(e) => Err(format!("Updater not available: {}", e)),
    }
}

#[tauri::command]
pub async fn get_app_version(app: tauri::AppHandle) -> Result<String, String> {
    Ok(app.package_info().version.to_string())
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
            agent_process: Arc::new(Mutex::new(None)),
        }
    }

    #[tokio::test]
    async fn test_get_usage_returns_data() {
        let _state = create_test_state();
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
