// Prevents additional console window on Windows in release
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use aic_core::{
    AuthenticationManager, ConfigLoader, GitHubAuthService, ProviderManager, ProviderUsage,
};
use std::process::Command;
use std::sync::Arc;
use std::time::Duration;
use tauri::menu::MenuEvent;
use tauri::{
    menu::{Menu, MenuItem},
    tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent},
    Emitter, Manager, Runtime, State, WebviewWindowBuilder,
};
use tokio::sync::{Mutex, RwLock};
use tokio::time::interval;

struct AppState {
    provider_manager: Arc<ProviderManager>,
    config_loader: Arc<ConfigLoader>,
    auth_manager: Arc<AuthenticationManager>,
    auto_refresh_enabled: Arc<Mutex<bool>>,
    device_flow_state: Arc<RwLock<Option<DeviceFlowState>>>,
}

#[derive(Clone)]
struct DeviceFlowState {
    device_code: String,
    user_code: String,
    verification_uri: String,
    interval: u64,
}

// Provider commands
#[tauri::command]
async fn get_usage(state: State<'_, AppState>) -> Result<Vec<ProviderUsage>, String> {
    let manager = &state.provider_manager;
    Ok(manager.get_all_usage(true).await)
}

#[tauri::command]
async fn refresh_usage(state: State<'_, AppState>) -> Result<Vec<ProviderUsage>, String> {
    let manager = &state.provider_manager;
    Ok(manager.get_all_usage(true).await)
}

// Preferences commands
#[tauri::command]
async fn load_preferences(state: State<'_, AppState>) -> Result<aic_core::AppPreferences, String> {
    let prefs = state.config_loader.load_preferences().await;
    Ok(prefs)
}

#[tauri::command]
async fn save_preferences(
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
async fn get_configured_providers(
    state: State<'_, AppState>,
) -> Result<Vec<aic_core::ProviderConfig>, String> {
    let configs = state.config_loader.load_config().await;
    Ok(configs)
}

#[tauri::command]
async fn save_provider_config(
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
async fn remove_provider_config(
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
async fn toggle_auto_refresh(state: State<'_, AppState>, enabled: bool) -> Result<(), String> {
    let mut auto_refresh = state.auto_refresh_enabled.lock().await;
    *auto_refresh = enabled;
    Ok(())
}

#[tauri::command]
async fn is_auto_refresh_enabled(state: State<'_, AppState>) -> Result<bool, String> {
    let auto_refresh = state.auto_refresh_enabled.lock().await;
    Ok(*auto_refresh)
}

// GitHub Authentication commands
#[tauri::command]
async fn is_github_authenticated(state: State<'_, AppState>) -> Result<bool, String> {
    Ok(state.auth_manager.is_authenticated())
}

#[tauri::command]
async fn initiate_github_login(
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
async fn complete_github_login(
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
async fn poll_github_token(
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
async fn logout_github(state: State<'_, AppState>) -> Result<(), String> {
    state
        .auth_manager
        .logout()
        .await
        .map_err(|e| format!("Logout failed: {}", e))
}

#[tauri::command]
async fn cancel_github_login(state: State<'_, AppState>) -> Result<(), String> {
    let mut flow_state = state.device_flow_state.write().await;
    *flow_state = None;
    Ok(())
}

// Window control commands
#[tauri::command]
async fn close_window(window: tauri::Window) -> Result<(), String> {
    let _ = window.close();
    Ok(())
}

#[tauri::command]
async fn minimize_window(window: tauri::Window) -> Result<(), String> {
    let _ = window.minimize();
    Ok(())
}

#[tauri::command]
async fn toggle_always_on_top(window: tauri::Window, enabled: bool) -> Result<(), String> {
    let _ = window.set_always_on_top(enabled);
    Ok(())
}

#[tauri::command]
async fn open_browser(url: String) -> Result<(), String> {
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

#[tauri::command]
async fn close_settings_window(window: tauri::Window) -> Result<(), String> {
    let _ = window.close();
    Ok(())
}

#[tauri::command]
async fn save_provider_configs(
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
async fn scan_for_api_keys(state: State<'_, AppState>) -> Result<Vec<aic_core::ProviderConfig>, String> {
    let configs = state.config_loader.load_config().await;
    Ok(configs)
}

#[tauri::command]
async fn check_github_login_status(state: State<'_, AppState>) -> Result<String, String> {
    use aic_core::TokenPollResult;
    
    let flow_state = state.device_flow_state.read().await;
    if let Some(flow) = flow_state.as_ref() {
        match state.auth_manager.poll_for_token(&flow.device_code).await {
            TokenPollResult::Token(_) => Ok("success".to_string()),
            TokenPollResult::Pending => Ok("pending".to_string()),
            TokenPollResult::SlowDown => Ok("slow_down".to_string()),
            TokenPollResult::Expired => Err("Token expired".to_string()),
            TokenPollResult::AccessDenied => Err("Access denied".to_string()),
            TokenPollResult::Error(msg) => Err(msg),
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
async fn discover_github_token() -> Result<TokenDiscoveryResult, String> {
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

#[derive(serde::Serialize)]
pub struct TokenDiscoveryResult {
    pub found: bool,
    pub token: String,
}

fn extract_pat(content: &str) -> Option<String> {
    if let Some(start) = content.find("github_pat_") {
        let rest = &content[start..];
        if let Some(end) = rest.find(|c: char| !c.is_alphanumeric() && c != '_' && c != '-') {
            Some(rest[..end].to_string())
        } else {
            Some(rest.to_string())
        }
    } else {
        None
    }
}

#[tauri::command]
async fn check_for_updates() -> Result<UpdateCheckResult, String> {
    const CURRENT_VERSION: &str = "1.7.13";
    
    Ok(UpdateCheckResult {
        current_version: CURRENT_VERSION.to_string(),
        latest_version: CURRENT_VERSION.to_string(),
        update_available: false,
        download_url: String::new(),
    })
}

#[derive(serde::Serialize)]
pub struct UpdateCheckResult {
    pub current_version: String,
    pub latest_version: String,
    pub update_available: bool,
    pub download_url: String,
}

#[tauri::command]
async fn get_app_version() -> Result<String, String> {
    Ok("1.7.13".to_string())
}

#[tauri::command]
async fn open_settings_window(app: tauri::AppHandle) -> Result<(), String> {
    println!("Opening settings window...");

    // Check if settings window already exists
    if let Some(window) = app.get_webview_window("settings") {
        let _ = window.show();
        let _ = window.set_focus();
        return Ok(());
    }

    // Create new settings window
    let _ = WebviewWindowBuilder::new(
        &app,
        "settings",
        tauri::WebviewUrl::App("settings.html".into()),
    )
    .title("Settings")
    .inner_size(500.0, 550.0)
    .min_inner_size(400.0, 400.0)
    .center()
    .decorations(false)
    .transparent(true)
    .build()
    .map_err(|e| e.to_string())?;

    Ok(())
}

fn create_tray_menu<R: Runtime>(
    app: &tauri::AppHandle<R>,
) -> Result<Menu<R>, Box<dyn std::error::Error>> {
    let show_i = MenuItem::with_id(app, "show", "Show", true, None::<&str>)?;
    let refresh_i = MenuItem::with_id(app, "refresh", "Refresh", true, None::<&str>)?;
    let auto_refresh_i =
        MenuItem::with_id(app, "auto_refresh", "Auto Refresh", true, None::<&str>)?;
    let settings_i = MenuItem::with_id(app, "settings", "Settings", true, None::<&str>)?;
    let quit_i = MenuItem::with_id(app, "quit", "Quit", true, None::<&str>)?;

    let menu = Menu::with_items(
        app,
        &[
            &show_i,
            &refresh_i,
            &auto_refresh_i,
            &MenuItem::with_id(app, "separator1", "---", false, None::<&str>)?,
            &settings_i,
            &MenuItem::with_id(app, "separator2", "---", false, None::<&str>)?,
            &quit_i,
        ],
    )?;

    Ok(menu)
}

#[tokio::main]
async fn main() {
    let client = reqwest::Client::new();
    let provider_manager = Arc::new(ProviderManager::new(client.clone()));
    let config_loader = Arc::new(ConfigLoader::new(client.clone()));
    let auth_service = Arc::new(GitHubAuthService::new(client));
    let auth_manager = Arc::new(AuthenticationManager::new(
        auth_service.clone(),
        config_loader.clone(),
    ));

    // Initialize auth manager from existing config
    auth_manager.initialize_from_config().await;

    // Start auto-refresh background task
    let auto_refresh_enabled = Arc::new(Mutex::new(false));
    let manager_clone = provider_manager.clone();
    let auto_refresh_clone = auto_refresh_enabled.clone();

    tokio::spawn(async move {
        let mut interval = interval(Duration::from_secs(300)); // 5 minutes

        loop {
            interval.tick().await;

            let enabled = *auto_refresh_clone.lock().await;
            if enabled {
                // Refresh usage in background
                let _ = manager_clone.get_all_usage(true).await;
            }
        }
    });

    tauri::Builder::default()
        .manage(AppState {
            provider_manager,
            config_loader,
            auth_manager,
            auto_refresh_enabled,
            device_flow_state: Arc::new(RwLock::new(None)),
        })
        .plugin(tauri_plugin_shell::init())
        // .plugin(tauri_plugin_updater::Builder::new().build())
        .invoke_handler(tauri::generate_handler![
            // Provider commands
            get_usage,
            refresh_usage,
            // Preferences commands
            load_preferences,
            save_preferences,
            // Config commands
            get_configured_providers,
            save_provider_config,
            remove_provider_config,
            // Auto-refresh commands
            toggle_auto_refresh,
            is_auto_refresh_enabled,
            // GitHub Authentication commands
            is_github_authenticated,
            initiate_github_login,
            complete_github_login,
            poll_github_token,
            logout_github,
            cancel_github_login,
            // Window control commands
            close_window,
            minimize_window,
            toggle_always_on_top,
            // Browser command
            open_browser,
            // Settings commands
            close_settings_window,
            open_settings_window,
            save_provider_configs,
            scan_for_api_keys,
            check_github_login_status,
            discover_github_token,
            check_for_updates,
            get_app_version,
        ])
        .setup(|app| {
            // Create tray menu
            let menu = create_tray_menu(app.handle())?;

            // Build tray icon
            let _tray = TrayIconBuilder::new()
                .icon(app.default_window_icon().unwrap().clone())
                .menu(&menu)
                .show_menu_on_left_click(true)
                .on_menu_event(|app: &tauri::AppHandle, event: MenuEvent| {
                    match event.id().as_ref() {
                        "show" => {
                            if let Some(window) = app.get_webview_window("main") {
                                let _ = window.show();
                                let _ = window.set_focus();
                            }
                        }
                        "refresh" => {
                            if let Some(window) = app.get_webview_window("main") {
                                let _ = window.emit("refresh-requested", ());
                            }
                        }
                        "auto_refresh" => {
                            if let Some(window) = app.get_webview_window("main") {
                                let _ = window.emit("toggle-auto-refresh", ());
                            }
                        }
                        "settings" => {
                            // Open settings window
                            if app.get_webview_window("settings").is_none() {
                                let _ = WebviewWindowBuilder::new(
                                    app,
                                    "settings",
                                    tauri::WebviewUrl::App("settings.html".into()),
                                )
                                .title("Settings")
                                .inner_size(500.0, 550.0)
                                .min_inner_size(400.0, 400.0)
                                .center()
                                .decorations(false)
                                .transparent(true)
                                .build();
                            } else if let Some(window) = app.get_webview_window("settings") {
                                let _ = window.show();
                                let _ = window.set_focus();
                            }
                        }
                        "quit" => {
                            app.exit(0);
                        }
                        _ => {}
                    }
                })
                .on_tray_icon_event(|tray: &tauri::tray::TrayIcon, event: TrayIconEvent| {
                    if let TrayIconEvent::Click {
                        button: MouseButton::Left,
                        button_state: MouseButtonState::Up,
                        ..
                    } = event
                    {
                        let app = tray.app_handle();
                        if let Some(window) = app.get_webview_window("main") {
                            let _ = window.show();
                            let _ = window.set_focus();
                        }
                    }
                })
                .build(app)?;

            // Ensure main window is shown
            if let Some(window) = app.get_webview_window("main") {
                window.show()?;
                window.set_focus()?;
                println!("Main window shown successfully");
            } else {
                println!("WARNING: Main window not found!");
            }

            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
