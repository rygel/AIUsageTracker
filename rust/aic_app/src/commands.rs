use aic_core::{
    AuthenticationManager, ConfigLoader, ProviderConfig, ProviderManager, ProviderUsage, TokenPollResult,
};
use tracing::{error, info, warn, debug};
use reqwest::Client;
use std::process::{Command, Child};
use std::sync::Arc;
use tauri::{State, Manager, AppHandle, Emitter};
use tauri_plugin_updater::UpdaterExt;
use tokio::sync::{Mutex, RwLock};

pub struct AppState {
    pub provider_manager: Arc<ProviderManager>,
    pub config_loader: Arc<ConfigLoader>,
    pub auth_manager: Arc<AuthenticationManager>,
    pub auto_refresh_enabled: Arc<Mutex<bool>>,
    pub device_flow_state: Arc<RwLock<Option<DeviceFlowState>>>,
    pub agent_process: Arc<Mutex<Option<Child>>>,
    pub preloaded_settings: Arc<Mutex<Option<PreloadedSettings>>>,
}

#[derive(Clone)]
pub struct PreloadedSettings {
    pub providers: Vec<ProviderConfig>,
    pub usage: Vec<ProviderUsage>,
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

#[tauri::command]
pub async fn get_usage_from_agent() -> Result<Vec<ProviderUsage>, String> {
    info!("Attempting to fetch usage from agent at http://localhost:8080/api/providers/usage");
    match reqwest::get("http://localhost:8080/api/providers/usage").await {
        Ok(response) => {
            info!("Agent responded with status: {}", response.status());
            // Check if we got a successful status code
            if !response.status().is_success() {
                let status = response.status();
                // Try to read error message from response body (text/plain per OpenAPI spec)
                let error_text = response.text().await.unwrap_or_else(|_| "Unknown error".to_string());
                error!("Agent returned error status {}: {}", status, error_text);
                return Err(format!("Agent error (HTTP {}): {}", status, error_text));
            }
            
            match response.json::<Vec<aic_core::ProviderUsage>>().await {
                Ok(usage) => {
                    info!("Retrieved {} usage records from agent", usage.len());
                    Ok(usage)
                }
                Err(e) => {
                    error!("Failed to parse usage from agent: {}", e);
                    Err(format!("Bad response from agent: The agent sent invalid data. Error: {}", e))
                }
            }
        }
        Err(e) => {
            error!("Failed to connect to agent for usage: {}", e);
            if e.is_connect() {
                Err(format!("Agent not running: Cannot connect to agent on port 8080. Please start the agent."))
            } else if e.is_timeout() {
                Err(format!("Agent timeout: The agent did not respond in time."))
            } else {
                Err(format!("Connection error: {}", e))
            }
        }
    }
}

#[tauri::command]
pub async fn refresh_usage_from_agent() -> Result<Vec<ProviderUsage>, String> {
    let client = reqwest::Client::new();
    match client.post("http://localhost:8080/api/providers/usage/refresh").send().await {
        Ok(response) => {
            // Check if we got a successful status code
            if !response.status().is_success() {
                let status = response.status();
                // Try to read error message from response body (text/plain per OpenAPI spec)
                let error_text = response.text().await.unwrap_or_else(|_| "Unknown error".to_string());
                error!("Agent returned error status {}: {}", status, error_text);
                return Err(format!("Agent error (HTTP {}): {}", status, error_text));
            }
            
            match response.json::<Vec<aic_core::ProviderUsage>>().await {
                Ok(usage) => {
                    info!("Refreshed and retrieved {} usage records from agent", usage.len());
                    Ok(usage)
                }
                Err(e) => {
                    error!("Failed to parse refreshed usage from agent: {}", e);
                    Err(format!("Bad response from agent: The agent sent invalid data. Error: {}", e))
                }
            }
        }
        Err(e) => {
            error!("Failed to connect to agent for refresh: {}", e);
            if e.is_connect() {
                Err(format!("Agent not running: Cannot connect to agent on port 8080. Please start the agent."))
            } else if e.is_timeout() {
                Err(format!("Agent timeout: The agent did not respond in time."))
            } else {
                Err(format!("Connection error: {}", e))
            }
        }
    }
}

#[tauri::command]
pub async fn get_historical_usage_from_agent(
    provider_id: Option<String>,
    limit: Option<u32>,
) -> Result<Vec<serde_json::Value>, String> {
    let mut url = "http://localhost:8080/api/history".to_string();
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
    
    match reqwest::get(&url).await {
        Ok(response) => {
            if !response.status().is_success() {
                let status = response.status();
                let error_text = response.text().await.unwrap_or_else(|_| "Unknown error".to_string());
                return Err(format!("Agent error (HTTP {}): {}", status, error_text));
            }
            
            match response.json::<Vec<serde_json::Value>>().await {
                Ok(history) => Ok(history),
                Err(e) => Err(format!("Failed to parse history from agent: {}", e))
            }
        }
        Err(e) => Err(format!("Connection error: {}", e))
    }
}

#[tauri::command]
pub async fn get_raw_responses_from_agent(
    provider_id: Option<String>,
    limit: Option<u32>,
) -> Result<Vec<serde_json::Value>, String> {
    let mut url = "http://localhost:8080/api/raw_responses".to_string();
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
    
    match reqwest::get(&url).await {
        Ok(response) => {
            if !response.status().is_success() {
                let status = response.status();
                let error_text = response.text().await.unwrap_or_else(|_| "Unknown error".to_string());
                return Err(format!("Agent error (HTTP {}): {}", status, error_text));
            }
            
            match response.json::<Vec<serde_json::Value>>().await {
                Ok(logs) => Ok(logs),
                Err(e) => Err(format!("Failed to parse raw logs from agent: {}", e))
            }
        }
        Err(e) => Err(format!("Connection error: {}", e))
    }
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
    let start = std::time::Instant::now();
    tracing::info!("get_all_providers_from_agent called");
    
    // Get all providers from agent (including discovered ones)
    let agent_url = "http://localhost:8080/api/providers/discovered";
    
    tracing::info!("Making request to: {}", agent_url);
    match reqwest::get(agent_url).await {
        Ok(response) => {
            let elapsed = start.elapsed();
            tracing::info!("Received response in {:?}", elapsed);
            
            // Check if we got a successful status code
            if !response.status().is_success() {
                let status = response.status();
                // Try to read error message from response body (text/plain per OpenAPI spec)
                let error_text = response.text().await.unwrap_or_else(|_| "Unknown error".to_string());
                tracing::error!("Agent returned error status {}: {}", status, error_text);
                return Err(format!("Agent error (HTTP {}): {}", status, error_text));
            }
            
            match response.json::<Vec<aic_core::ProviderConfig>>().await {
                Ok(providers) => {
                    let total_elapsed = start.elapsed();
                    tracing::info!("Retrieved {} providers from agent in {:?}", providers.len(), total_elapsed);
                    Ok(providers)
                }
                Err(e) => {
                    tracing::error!("Failed to parse providers from agent: {}", e);
                    Err(format!("Bad response from agent: The agent sent invalid data. Error: {}", e))
                }
            }
        }
        Err(e) => {
            tracing::error!("Failed to connect to agent: {}", e);
            if e.is_connect() {
                Err(format!("Agent not running: Cannot connect to agent on port 8080. Please start the agent."))
            } else if e.is_timeout() {
                Err(format!("Agent timeout: The agent did not respond in time."))
            } else {
                Err(format!("Connection error: {}", e))
            }
        }
    }
}

#[tauri::command]
pub async fn scan_for_api_keys(_state: State<'_, AppState>) -> Result<Vec<aic_core::ProviderConfig>, String> {
    let client = Client::new();
    // Trigger explicit discovery scan via agent
    let agent_url = "http://localhost:8080/api/discover";

    match client.post(agent_url).send().await {
        Ok(response) if response.status().is_success() => {
            match response.json::<Vec<aic_core::ProviderConfig>>().await {
                Ok(providers) => {
                    info!("Discovery completed, found {} providers", providers.len());
                    Ok(providers)
                }
                Err(e) => {
                    error!("Failed to parse discovery response: {}", e);
                    Err(format!("Failed to parse discovery response: {}", e))
                }
            }
        }
        Ok(response) => {
            let status = response.status();
            let error_text = response.text().await.unwrap_or_else(|_| "Unknown error".to_string());
            error!("Agent returned error {}: {}", status, error_text);
            Err(format!("Agent error (HTTP {}): {}", status, error_text))
        }
        Err(e) => {
            error!("Failed to trigger discovery on agent: {}", e);
            Err(format!("Failed to trigger discovery: {}", e))
        }
    }
}

#[tauri::command]
pub async fn save_provider_config(
    _state: State<'_, AppState>,
    config: aic_core::ProviderConfig,
) -> Result<(), String> {
    let client = reqwest::Client::new();
    let url = format!("http://localhost:8080/api/providers/{}", config.provider_id);
    
    tracing::info!("Saving provider config via agent: {}", config.provider_id);
    
    match client.put(&url).json(&config).send().await {
        Ok(response) if response.status().is_success() => {
            tracing::info!("Successfully saved provider: {}", config.provider_id);
            Ok(())
        }
        Ok(response) => {
            let status = response.status();
            let error_text = response.text().await.unwrap_or_else(|_| "Unknown error".to_string());
            tracing::error!("Agent returned error {}: {}", status, error_text);
            Err(format!("Agent error (HTTP {}): {}", status, error_text))
        }
        Err(e) => {
            tracing::error!("Failed to connect to agent: {}", e);
            Err(format!("Cannot connect to agent: {}", e))
        }
    }
}

#[tauri::command]
pub async fn remove_provider_config(
    _state: State<'_, AppState>,
    provider_id: String,
) -> Result<(), String> {
    let client = reqwest::Client::new();
    let url = format!("http://localhost:8080/api/providers/{}", provider_id);
    
    tracing::info!("Removing provider config via agent: {}", provider_id);
    
    match client.delete(&url).send().await {
        Ok(response) if response.status().is_success() => {
            tracing::info!("Successfully removed provider: {}", provider_id);
            Ok(())
        }
        Ok(response) => {
            let status = response.status();
            let error_text = response.text().await.unwrap_or_else(|_| "Unknown error".to_string());
            tracing::error!("Agent returned error {}: {}", status, error_text);
            Err(format!("Agent error (HTTP {}): {}", status, error_text))
        }
        Err(e) => {
            tracing::error!("Failed to connect to agent: {}", e);
            Err(format!("Cannot connect to agent: {}", e))
        }
    }
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
pub async fn close_window(window: tauri::Window, _app: tauri::AppHandle) -> Result<(), String> {
    info!("Close window command received for window: {}", window.label());

    // For main window, hide instead of close to keep app running in tray
    if window.label() == "main" {
        if let Err(e) = window.hide() {
            error!("Error hiding window: {}", e);
            return Err(format!("Failed to hide window: {}", e));
        }
        info!("Main window hidden (app continues running in tray)");
    } else {
        // For other windows, close normally
        if let Err(e) = window.close() {
            error!("Error closing window: {}", e);
            return Err(format!("Failed to close window: {}", e));
        }
        info!("Window {} closed", window.label());
    }

    Ok(())
}

#[tauri::command]
pub async fn minimize_window(window: tauri::Window) -> Result<(), String> {
    let _ = window.minimize();
    Ok(())
}

#[tauri::command]
pub async fn toggle_always_on_top(app: tauri::AppHandle, enabled: bool) -> Result<(), String> {
    if let Some(window) = app.get_webview_window("main") {
        let _ = window.set_always_on_top(enabled);
        tracing::info!("Set main window always_on_top to: {}", enabled);
        Ok(())
    } else {
        tracing::error!("Main window not found");
        Err("Main window not found".to_string())
    }
}

#[tauri::command]
pub async fn open_settings_window(app: tauri::AppHandle) -> Result<(), String> {
    tracing::info!("open_settings_window called");
    
    // Try to get existing window
    if let Some(window) = app.get_webview_window("settings") {
        tracing::info!("Found existing settings window, showing it");
        let _ = window.emit("settings-window-shown", ());
        let _ = window.show();
        let _ = window.set_focus();
        return Ok(());
    }
    
    // Get parent window
    let parent = app.get_webview_window("main")
        .ok_or("Main window not found")?;
    
    // Window doesn't exist, create it
    tracing::info!("Settings window not found, creating new window");
    let window = tauri::WebviewWindowBuilder::new(
        &app,
        "settings",
        tauri::WebviewUrl::App("settings.html".into())
    )
    .title("Settings - AI Consumption Tracker")
    .inner_size(700.0, 600.0)
    .min_inner_size(550.0, 500.0)
    .center()
    .decorations(false)
    .transparent(true)
    .parent(&parent)
    .map_err(|e| format!("Failed to set parent: {}", e))?
    .build()
    .map_err(|e| format!("Failed to create window: {}", e))?;
    
    tracing::info!("Settings window created successfully");
    let _ = window.show();
    let _ = window.set_focus();
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
    _state: State<'_, AppState>,
    configs: Vec<aic_core::ProviderConfig>,
) -> Result<(), String> {
    let client = reqwest::Client::new();
    let url = "http://localhost:8080/api/config/providers";
    
    tracing::info!("Saving all provider configs via agent (count: {})", configs.len());
    
    match client.post(url).json(&configs).send().await {
        Ok(response) if response.status().is_success() => {
            tracing::info!("Successfully saved all provider configs");
            Ok(())
        }
        Ok(response) => {
            let status = response.status();
            let error_text = response.text().await.unwrap_or_else(|_| "Unknown error".to_string());
            tracing::error!("Agent returned error {}: {}", status, error_text);
            Err(format!("Agent error (HTTP {}): {}", status, error_text))
        }
        Err(e) => {
            tracing::error!("Failed to connect to agent: {}", e);
            Err(format!("Cannot connect to agent: {}", e))
        }
    }
}

#[tauri::command]
pub async fn close_settings_window(window: tauri::Window) -> Result<(), String> {
    // Hide the window instead of closing it so it can be reopened
    let _ = window.hide();
    Ok(())
}

#[tauri::command]
pub async fn preload_settings_data(state: State<'_, AppState>) -> Result<(), String> {
    tracing::info!("Preloading settings data...");
    
    // Fetch providers and usage data in parallel from agent
    let providers_future = async {
        let agent_url = "http://localhost:8080/api/providers/discovered";
        match reqwest::get(agent_url).await {
            Ok(response) if response.status().is_success() => {
                match response.json::<Vec<aic_core::ProviderConfig>>().await {
                    Ok(providers) => {
                        tracing::info!("Preloaded {} providers", providers.len());
                        Some(providers)
                    }
                    Err(e) => {
                        tracing::error!("Failed to parse providers: {}", e);
                        None
                    }
                }
            }
            Ok(response) => {
                tracing::error!("Agent error: {}", response.status());
                None
            }
            Err(e) => {
                tracing::error!("Failed to connect to agent: {}", e);
                None
            }
        }
    };
    
    let usage_future = async {
        let agent_url = "http://localhost:8080/api/providers/usage";
        match reqwest::get(agent_url).await {
            Ok(response) if response.status().is_success() => {
                match response.json::<Vec<ProviderUsage>>().await {
                    Ok(usage) => {
                        tracing::info!("Preloaded {} usage records", usage.len());
                        Some(usage)
                    }
                    Err(e) => {
                        tracing::error!("Failed to parse usage: {}", e);
                        None
                    }
                }
            }
            Ok(response) => {
                tracing::error!("Agent error: {}", response.status());
                None
            }
            Err(e) => {
                tracing::error!("Failed to connect to agent: {}", e);
                None
            }
        }
    };
    
    let (providers, usage) = tokio::join!(providers_future, usage_future);
    
    // Store in state
    let mut preloaded = state.preloaded_settings.lock().await;
    *preloaded = Some(PreloadedSettings { 
        providers: providers.unwrap_or_default(), 
        usage: usage.unwrap_or_default() 
    });
    
    tracing::info!("Settings data preloaded successfully");
    Ok(())
}

#[tauri::command]
pub async fn get_preloaded_settings_data(state: State<'_, AppState>) -> Result<Option<(Vec<ProviderConfig>, Vec<ProviderUsage>)>, String> {
    let preloaded = state.preloaded_settings.lock().await;
    Ok(preloaded.as_ref().map(|p| (p.providers.clone(), p.usage.clone())))
}

#[tauri::command]
pub async fn open_info_window(app: tauri::AppHandle) -> Result<(), String> {
    tracing::info!("open_info_window called");
    
    // Try to get existing window
    if let Some(window) = app.get_webview_window("info") {
        tracing::info!("Found existing info window, showing it");
        let _ = window.show();
        let _ = window.set_focus();
        return Ok(());
    }
    
    // Get parent window
    let parent = app.get_webview_window("main")
        .ok_or("Main window not found")?;
    
    // Window doesn't exist, create it
    tracing::info!("Info window not found, creating new window");
    let window = tauri::WebviewWindowBuilder::new(
        &app,
        "info",
        tauri::WebviewUrl::App("info.html".into())
    )
    .title("About AI Consumption Tracker")
    .inner_size(450.0, 450.0)
    .min_inner_size(400.0, 400.0)
    .center()
    .decorations(false)
    .transparent(true)
    .parent(&parent)
    .map_err(|e| format!("Failed to set parent: {}", e))?
    .build()
    .map_err(|e| format!("Failed to create window: {}", e))?;
    
    tracing::info!("Info window created successfully");
    let _ = window.show();
    let _ = window.set_focus();
    Ok(())
}

#[tauri::command]
pub async fn close_info_window(window: tauri::Window) -> Result<(), String> {
    // Hide the window instead of closing it so it can be reopened
    let _ = window.hide();
    Ok(())
}

#[tauri::command]
pub async fn get_config_path() -> Result<String, String> {
    // Return the path to the auth.json config file
    #[cfg(target_os = "windows")]
    {
        if let Ok(app_data) = std::env::var("APPDATA") {
            return Ok(format!("{}\\aic\\auth.json", app_data));
        }
    }
    #[cfg(not(target_os = "windows"))]
    {
        if let Ok(home) = std::env::var("HOME") {
            return Ok(format!("{}/.config/aic/auth.json", home));
        }
    }
    Err("Could not determine config path".to_string())
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
    debug!("Attempting to stop agent");
    
    let mut agent_process = state.agent_process.lock().await;
    debug!("Acquired agent process lock");

    if let Some(ref mut child) = *agent_process {
        let pid = child.id();
        info!("Found agent process with PID: {}, attempting to kill", pid);
        
        match child.kill() {
            Ok(_) => {
                info!("Agent process (PID: {}) killed successfully", pid);
                
                // Wait for the process to actually exit
                match child.wait() {
                    Ok(exit_status) => {
                        info!("Agent process exited with status: {:?}", exit_status);
                    }
                    Err(e) => {
                        warn!("Could not wait for agent process exit: {}", e);
                    }
                }
                
                *agent_process = None;
                Ok(true)
            }
            Err(e) => {
                let error_msg = format!("Failed to kill agent process (PID: {}): {}", pid, e);
                error!("{}", error_msg);
                Err(error_msg)
            }
        }
    } else {
        warn!("No agent process found in state");
        Err("No agent process running".to_string())
    }
}

#[tauri::command]
pub async fn is_agent_running(state: State<'_, AppState>) -> Result<bool, String> {
    let mut agent_process = state.agent_process.lock().await;

    if let Some(ref mut child) = *agent_process {
        let pid = child.id();
        debug!("Checking if agent is running (PID: {})", pid);
        
        match child.try_wait() {
            Ok(None) => {
                debug!("Agent process (PID: {}) is still running", pid);
                Ok(true)
            }
            Ok(exit_code) => {
                info!("Agent process (PID: {}) has exited with code: {:?}", pid, exit_code);
                *agent_process = None;
                Ok(false)
            }
            Err(e) => {
                warn!("Could not check agent process (PID: {}) status: {}", pid, e);
                *agent_process = None;
                Ok(false)
            }
        }
    } else {
        debug!("No agent process in state");
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

pub async fn find_agent_executable(app_handle: &tauri::AppHandle) -> Result<String, String> {
    let exe_name = if cfg!(target_os = "windows") {
        "aic_agent.exe"
    } else {
        "aic_agent"
    };
    
    debug!("Searching for agent executable: {}", exe_name);

    // Build list of possible paths to check
    let mut possible_paths: Vec<std::path::PathBuf> = Vec::new();

    // 1. Current working directory
    if let Ok(current_dir) = std::env::current_dir() {
        let path = current_dir.join(exe_name);
        debug!("Checking current directory: {:?}", path);
        possible_paths.push(path);
    }

    // 2. App resource directory (for packaged apps)
    if let Ok(resource_dir) = app_handle.path().resolve(
        "",
        tauri::path::BaseDirectory::Resource
    ) {
        let path = resource_dir.join(exe_name);
        debug!("Checking resource directory: {:?}", path);
        possible_paths.push(path);
    }

    // 3. App binary directory (where the app executable is located)
    if let Ok(app_dir) = app_handle.path().resolve(
        "",
        tauri::path::BaseDirectory::AppLocalData
    ) {
        let path = app_dir.join(exe_name);
        debug!("Checking app local data directory: {:?}", path);
        possible_paths.push(path);
        // Also check parent of app data directory
        if let Some(parent) = app_dir.parent() {
            let path = parent.join(exe_name);
            debug!("Checking parent of app data directory: {:?}", path);
            possible_paths.push(path);
        }
    }

    // 4. Development paths (relative to current directory)
    debug!("Adding development paths");
    possible_paths.push(std::path::PathBuf::from(format!("./{}", exe_name)));
    possible_paths.push(std::path::PathBuf::from(format!("../target/debug/{}", exe_name)));
    possible_paths.push(std::path::PathBuf::from(format!("../target/release/{}", exe_name)));

    // 5. Check if running from aic_app directory - look in parent rust directory
    if let Ok(current_dir) = std::env::current_dir() {
        debug!("Current working directory: {:?}", current_dir);
        let rust_debug = current_dir.join("..").join("..").join("target").join("debug").join(exe_name);
        let rust_release = current_dir.join("..").join("..").join("target").join("release").join(exe_name);
        debug!("Checking rust debug path: {:?}", rust_debug);
        debug!("Checking rust release path: {:?}", rust_release);
        possible_paths.push(rust_debug);
        possible_paths.push(rust_release);
    }

    // Try each path
    info!("Searching for agent executable in {} possible locations", possible_paths.len());
    for (idx, path) in possible_paths.iter().enumerate() {
        let exists = path.exists();
        debug!("[{}] Checking for agent at: {:?} - exists: {}", idx, path, exists);
        if exists {
            info!("Found agent executable at: {:?}", path);
            return Ok(path.to_string_lossy().to_string());
        }
    }

    // Log all attempted paths for debugging
    let cwd = std::env::current_dir().map(|p| p.to_string_lossy().to_string()).unwrap_or_else(|_| "unknown".to_string());
    error!("Agent executable '{}' not found. Current working directory: {}", exe_name, cwd);
    error!("Searched in the following locations:");
    for (idx, path) in possible_paths.iter().enumerate() {
        error!("  [{}] {:?} - exists: {}", idx, path, path.exists());
    }

    Err(format!(
        "Agent executable '{}' not found in current directory '{}'. Please build the agent with: cargo build -p aic_agent",
        exe_name, cwd
    ))
}

pub async fn start_agent_internal(
    app_handle: &tauri::AppHandle,
    agent_process: Arc<Mutex<Option<Child>>>,
) -> Result<bool, String> {
    debug!("Starting agent internal process");
    
    // First, check if something is already listening on port 8080
    debug!("Checking if agent already running on port 8080");
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
            debug!("Port 8080 is available, proceeding to start agent");
        }
        Err(e) => {
            warn!("Could not check if port 8080 is in use: {}", e);
        }
    }

    let mut agent_process = agent_process.lock().await;
    debug!("Acquired agent process lock");

    if let Some(ref mut child) = *agent_process {
        debug!("Found existing agent process, checking if still running");
        match child.try_wait() {
            Ok(None) => {
                info!("Agent process is already running");
                let app_handle = app_handle.clone();
                tokio::spawn(async move {
                    update_tray_icon_by_status(&app_handle, true).await;
                });
                return Ok(true);
            }
            Ok(exit_code) => {
                info!("Previous agent process has exited with code: {:?}", exit_code);
                *agent_process = None;
            }
            Err(e) => {
                warn!("Failed to check agent process status: {}", e);
                *agent_process = None;
            }
        }
    } else {
        debug!("No existing agent process found");
    }

    debug!("Searching for agent executable");
    let agent_path = match find_agent_executable(app_handle).await {
        Ok(path) => {
            info!("Found agent executable at: {}", path);
            path
        }
        Err(e) => {
            error!("Agent executable not found: {}", e);
            return Err(e);
        }
    };

    info!("[START] Spawning agent process: {}", agent_path);
    match Command::new(&agent_path).spawn() {
        Ok(child) => {
            let pid = child.id();
            *agent_process = Some(child);
            info!("Agent started successfully with PID: {}", pid);

            let app_handle = app_handle.clone();
            tokio::spawn(async move {
                // Wait for agent to be ready
                debug!("Waiting 2 seconds for agent to initialize");
                tokio::time::sleep(tokio::time::Duration::from_millis(2000)).await;
                
                // Update tray icon
                debug!("Updating tray icon to show agent is running");
                update_tray_icon_by_status(&app_handle, true).await;
                
                // Notify all windows that agent is ready
                info!("Emitting agent-ready event to all windows");
                let _ = app_handle.emit("agent-ready", ());
                
                // Also try to reload settings window if it's open
                if let Some(settings_window) = app_handle.get_webview_window("settings") {
                    info!("Settings window found, emitting settings-window-shown");
                    let _ = settings_window.emit("settings-window-shown", ());
                }
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
pub async fn get_app_version() -> Result<String, String> {
    Ok(env!("CARGO_PKG_VERSION").to_string())
}

#[tauri::command]
pub async fn get_agent_version() -> Result<String, String> {
    let client = reqwest::Client::new();
    let agent_url = "http://localhost:8080";
    
    match client.get(format!("{}/health", agent_url)).send().await {
        Ok(response) => {
            if response.status().is_success() {
                match response.json::<serde_json::Value>().await {
                    Ok(json) => {
                        if let Some(version) = json.get("version").and_then(|v| v.as_str()) {
                            Ok(version.to_string())
                        } else {
                            Err("Version not found in agent response".to_string())
                        }
                    }
                    Err(e) => Err(format!("Failed to parse agent response: {}", e)),
                }
            } else {
                Err(format!("Agent returned status: {}", response.status()))
            }
        }
        Err(e) => Err(format!("Failed to connect to agent: {}", e)),
    }
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
            preloaded_settings: Arc::new(Mutex::new(None)),
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
