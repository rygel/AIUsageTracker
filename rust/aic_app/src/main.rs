// Prevents additional console window on Windows in release
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use aic_core::{
    AuthenticationManager, ConfigLoader, GitHubAuthService, ProviderManager,
};
use aic_app::commands::*;
use std::sync::Arc;
use std::time::Duration;

use tauri::{
    menu::{Menu, MenuItem},
    tray::TrayIconBuilder,
    Emitter, Manager, Runtime, AppHandle,
};
use tauri_plugin_updater::UpdaterExt;
use tokio::sync::{Mutex, RwLock};
use tokio::time::interval;

async fn check_and_update_tray_status(app_handle: &AppHandle) {
    let state = app_handle.state::<AppState>();
    let mut agent_process = state.agent_process.lock().await;

    let is_connected = if let Some(ref mut child) = *agent_process {
        match child.try_wait() {
            Ok(None) => true,
            Ok(_) => {
                *agent_process = None;
                false
            }
            Err(_) => {
                *agent_process = None;
                false
            }
        }
    } else {
        false
    };

    update_tray_icon_by_status(app_handle, is_connected).await;
}

fn create_tray_menu<R: Runtime>(
    app: &tauri::AppHandle<R>,
) -> Result<Menu<R>, Box<dyn std::error::Error>> {
    let show_i = MenuItem::with_id(app, "show", "Show", true, None::<&str>)?;
    let refresh_i = MenuItem::with_id(app, "refresh", "Refresh", true, None::<&str>)?;
    let auto_refresh_i =
        MenuItem::with_id(app, "auto_refresh", "Auto Refresh", true, None::<&str>)?;
    let agent_start_i = MenuItem::with_id(app, "start_agent", "Start Agent", true, None::<&str>)?;
    let agent_stop_i = MenuItem::with_id(app, "stop_agent", "Stop Agent", true, None::<&str>)?;
    let settings_i = MenuItem::with_id(app, "settings", "Settings", true, None::<&str>)?;
    let quit_i = MenuItem::with_id(app, "quit", "Quit", true, None::<&str>)?;

    let menu = Menu::with_items(
        app,
        &[
            &show_i,
            &refresh_i,
            &auto_refresh_i,
            &MenuItem::with_id(app, "separator1", "---", false, None::<&str>)?,
            &agent_start_i,
            &agent_stop_i,
            &MenuItem::with_id(app, "separator2", "---", false, None::<&str>)?,
            &settings_i,
            &MenuItem::with_id(app, "separator3", "---", false, None::<&str>)?,
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
    let auth_manager_clone = auth_manager.clone();
    tokio::spawn(async move {
        auth_manager_clone.initialize_from_config().await;
    });

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
            agent_process: Arc::new(Mutex::new(None)),
        })
        .plugin(tauri_plugin_shell::init())
        .plugin(tauri_plugin_updater::Builder::new().build())
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
            // Agent management commands
            start_agent,
            stop_agent,
            is_agent_running,
            is_agent_running_http,
            get_agent_status_details,
            get_all_providers_from_agent,
            // Update management commands
            get_app_version,
            check_for_updates,
            install_update,
        ])
        .setup(|app| {
            // Create tray menu
            let menu = create_tray_menu(app.handle())?;

            // Build tray icon
            let _tray = TrayIconBuilder::new()
                .menu(&menu)
                .tooltip("AI Consumption Tracker")

                .on_menu_event(move |app, event| {
                    match event.id().as_ref() {
                        "show" => {
                            if let Some(window) = app.get_webview_window("main") {
                                let _: Result<(), _> = window.show();
                                let _: Result<(), _> = window.set_focus();
                            }
                        }
                        "refresh" => {
                            if let Some(window) = app.get_webview_window("main") {
                                let _: Result<(), _> = window.emit("refresh-requested", ());
                            }
                        }
                        "auto_refresh" => {
                            if let Some(window) = app.get_webview_window("main") {
                                let _: Result<(), _> = window.emit("toggle-auto-refresh", ());
                            }
                        }
                        "start_agent" => {
                            if let Some(window) = app.get_webview_window("main") {
                                let _: Result<(), _> = window.emit("start-agent", ());
                            }
                        }
                        "stop_agent" => {
                            if let Some(window) = app.get_webview_window("main") {
                                let _: Result<(), _> = window.emit("stop-agent", ());
                            }
                        }
                        "settings" => {
                            let _: Result<(), _> = app.emit("open-settings-window", ());
                        }
                        "quit" => {
                            app.exit(0);
                        }
                        _ => {}
                    }
                })
                .build(app)?;

            // Initial tray icon status check
            let app_handle = app.handle().clone();
            tokio::spawn(async move {
                tokio::time::sleep(tokio::time::Duration::from_millis(500)).await;
                check_and_update_tray_status(&app_handle).await;
            });

            // Periodic tray icon status update (every 30 seconds)
            let app_handle = app.handle().clone();
            tokio::spawn(async move {
                loop {
                    tokio::time::sleep(tokio::time::Duration::from_secs(30)).await;
                    check_and_update_tray_status(&app_handle).await;
                }
            });

            // Ensure main window is shown
            if let Some(window) = app.get_webview_window("main") {
                window.show()?;
                window.set_focus()?;
                println!("Main window shown successfully");
            } else {
                println!("WARNING: Main window not found!");
            }

            // Check for updates on startup (silent)
            let app_handle = app.handle().clone();
            tokio::spawn(async move {
                // Wait a moment for app to fully initialize
                tokio::time::sleep(tokio::time::Duration::from_secs(5)).await;
                
                if let Ok(updater) = app_handle.updater() {
                    match updater.check().await {
                        Ok(Some(update)) => {
                            log::info!("Update available: v{}", update.version);
                            // Optionally show notification or update tray menu
                        }
                        Ok(None) => {
                            log::debug!("No updates available");
                        }
                        Err(e) => {
                            log::error!("Failed to check for updates on startup: {}", e);
                        }
                    }
                }
            });

            // Do startup discovery once
            let config_loader = app.state::<AppState>().config_loader.clone();
            tokio::spawn(async move {
                tokio::time::sleep(tokio::time::Duration::from_millis(500)).await;

                log::info!("Performing startup configuration discovery...");
                let configs = config_loader.load_config().await;
                log::info!("Startup discovery found {} provider configurations", configs.len());
            });

            // Auto-start agent if not running
            let app_handle = app.handle().clone();
            let agent_process = app.state::<AppState>().agent_process.clone();
            tokio::spawn(async move {
                tokio::time::sleep(tokio::time::Duration::from_secs(2)).await;
                
                log::info!("Checking if agent is running on startup...");
                let is_running = match check_agent_status().await {
                    Ok(running) => running,
                    Err(e) => {
                        log::error!("Failed to check agent status: {}", e);
                        false
                    }
                };

                if !is_running {
                    log::info!("Agent not running, starting automatically...");
                    match start_agent_internal(&app_handle, agent_process).await {
                        Ok(started) => {
                            if started {
                                log::info!("Agent started successfully");
                            } else {
                                log::warn!("Agent failed to start");
                            }
                        }
                        Err(e) => {
                            log::error!("Failed to start agent: {}", e);
                        }
                    }
                }
            });

            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("error while running tauri application")
}