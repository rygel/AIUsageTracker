// Prevents additional console window on Windows in release
// Commented out for debug builds - uncomment for production
// #![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use aic_core::{
    AuthenticationManager, ConfigLoader, GitHubAuthService, ProviderManager,
};
use aic_app::commands::*;
use std::sync::Arc;
use std::time::Duration;

use tauri::{
    menu::{Menu, MenuItem},
    tray::{TrayIconBuilder, TrayIconEvent},
    Emitter, Manager, Runtime, AppHandle,
};
use tauri_plugin_updater::UpdaterExt;
use tokio::sync::{Mutex, RwLock};
use tokio::time::interval;
use tracing::{info, error, debug};

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
    let info_i = MenuItem::with_id(app, "info", "Info", true, None::<&str>)?;
    let exit_i = MenuItem::with_id(app, "exit", "Exit", true, None::<&str>)?;

    let menu = Menu::with_items(
        app,
        &[
            &show_i,
            &MenuItem::with_id(app, "separator1", "---", false, None::<&str>)?,
            &info_i,
            &MenuItem::with_id(app, "separator2", "---", false, None::<&str>)?,
            &exit_i,
        ],
    )?;

    Ok(menu)
}

#[tokio::main]
async fn main() {
    // Initialize tracing for console output
    tracing_subscriber::fmt()
        .with_env_filter(tracing_subscriber::EnvFilter::from_default_env()
            .add_directive(tracing::Level::INFO.into()))
        .init();
    
    info!("Starting AI Consumption Tracker application");

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
            get_usage_from_agent,
            refresh_usage_from_agent,
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
            reload_settings_window,
            save_provider_configs,
            // Info window commands
            open_info_window,
            close_info_window,
            get_config_path,
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
            get_agent_version,
            check_for_updates,
            install_update,
        ])
        .setup(|app| {
            // Create tray menu
            let menu = create_tray_menu(app.handle())?;

            // Build tray icon
            let tray = TrayIconBuilder::new()
                .menu(&menu)
                .tooltip("AI Consumption Tracker")
                .icon(app.default_window_icon().unwrap().clone())
                .on_menu_event(move |app, event| {
                    match event.id().as_ref() {
                        "show" => {
                            if let Some(window) = app.get_webview_window("main") {
                                let _: Result<(), _> = window.show();
                                let _: Result<(), _> = window.set_focus();
                            }
                        }
                        "info" => {
                            let app_clone = app.clone();
                            tokio::spawn(async move {
                                let _ = aic_app::commands::open_info_window(app_clone).await;
                            });
                        }
                        "exit" => {
                            app.exit(0);
                        }
                        _ => {}
                    }
                })
                .build(app)?;

            // Handle tray icon click events
            tray.on_tray_icon_event(|tray, event| {
                if let TrayIconEvent::Click { button: tauri::tray::MouseButton::Left, .. } = event {
                    // Show window on left click
                    if let Some(window) = tray.app_handle().get_webview_window("main") {
                        let _ = window.show();
                        let _ = window.set_focus();
                    }
                }
            });

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
                
                // Set window title with version number
                let version = env!("CARGO_PKG_VERSION");
                window.set_title(&format!("AI Consumption Tracker v{}", version))?;
                
                // Handle window close event - hide instead of close
                let window_clone = window.clone();
                window.on_window_event(move |event| {
                    if let tauri::WindowEvent::CloseRequested { api, .. } = event {
                        println!("Main window close requested - hiding instead");
                        api.prevent_close();
                        let _ = window_clone.hide();
                    }
                });
                
                println!("Main window shown successfully");
            } else {
                println!("WARNING: Main window not found!");
            }

            // Add close handler for settings window (hide instead of close)
            if let Some(settings_window) = app.get_webview_window("settings") {
                let settings_clone = settings_window.clone();
                settings_window.on_window_event(move |event| {
                    if let tauri::WindowEvent::CloseRequested { api, .. } = event {
                        println!("Settings window close requested - hiding instead");
                        api.prevent_close();
                        let _ = settings_clone.hide();
                    }
                });
                println!("Settings window close handler installed");
            }

            // Add close handler for info window (hide instead of close)
            if let Some(info_window) = app.get_webview_window("info") {
                let info_clone = info_window.clone();
                info_window.on_window_event(move |event| {
                    if let tauri::WindowEvent::CloseRequested { api, .. } = event {
                        println!("Info window close requested - hiding instead");
                        api.prevent_close();
                        let _ = info_clone.hide();
                    }
                });
                println!("Info window close handler installed");
            }

            // Check for updates on startup (silent)
            let app_handle = app.handle().clone();
            tokio::spawn(async move {
                // Wait a moment for app to fully initialize
                tokio::time::sleep(tokio::time::Duration::from_secs(5)).await;
                
                if let Ok(updater) = app_handle.updater() {
                    match updater.check().await {
                        Ok(Some(update)) => {
                            tracing::info!("Update available: v{}", update.version);
                            // Optionally show notification or update tray menu
                        }
                        Ok(None) => {
                            tracing::debug!("No updates available");
                        }
                        Err(e) => {
                            tracing::error!("Failed to check for updates on startup: {}", e);
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