// Prevents additional console window on Windows in release
// Commented out for debug builds - uncomment for production
// #![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use aic_core::{
    AuthenticationManager, ConfigLoader, GitHubAuthService, ProviderManager,
    ProviderConfig, ProviderUsage,
};
use aic_app::commands::*;
use clap::Parser;
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::{Duration, Instant};
use tracing_subscriber::{layer::SubscriberExt, util::SubscriberInitExt};

use tauri::{
    menu::{Menu, MenuItem},
    tray::{TrayIconBuilder, TrayIconEvent},
    Emitter, Manager, Runtime, AppHandle,
};
use tauri_plugin_updater::UpdaterExt;
use ctrlc;
use tokio::sync::{Mutex, RwLock};
use tokio::time::interval;
use tracing::{info, error, debug, warn};

// Global flag to prevent duplicate cleanup
static CLEANUP_DONE: AtomicBool = AtomicBool::new(false);

// App startup timing - use OnceLock to initialize on first access
static APP_START_TIME: std::sync::OnceLock<Instant> = std::sync::OnceLock::new();

fn get_app_start_time() -> Instant {
    *APP_START_TIME.get_or_init(Instant::now)
}

pub fn log_timing(label: &'static str) {
    let elapsed = get_app_start_time().elapsed();
    info!("[TIMING] {} at {:?}", label, elapsed);
}

fn clean_old_logs(log_dir: &std::path::Path) {
    use std::time::SystemTime;
    
    let cutoff = SystemTime::now()
        .checked_sub(Duration::from_secs(30 * 24 * 60 * 60))
        .unwrap_or(SystemTime::UNIX_EPOCH);
    
    if let Ok(entries) = std::fs::read_dir(log_dir) {
        for entry in entries.flatten() {
            if let Ok(metadata) = entry.metadata() {
                if let Ok(modified) = metadata.modified() {
                    if modified < cutoff {
                        let _ = std::fs::remove_file(entry.path());
                        info!("Removed old log file: {:?}", entry.path());
                    }
                }
            }
        }
    }
}

fn cleanup_and_exit(app: &tauri::AppHandle) {
    // Prevent duplicate cleanup
    if CLEANUP_DONE.compare_exchange(false, true, Ordering::SeqCst, Ordering::SeqCst).is_err() {
        return;
    }

    info!("Cleaning up and exiting...");

    // Close all webview windows
    let window_ids = ["main", "settings", "info"];
    for id in window_ids {
        if let Some(window) = app.get_webview_window(id) {
            let _ = window.close();
        }
    }

    // Remove tray icon
    let _ = app.remove_tray_by_id("main");

    info!("Cleanup complete - exiting");
    app.exit(0);
}

fn setup_signal_handlers() {
    ctrlc::set_handler(move || {
        if CLEANUP_DONE.compare_exchange(false, true, Ordering::SeqCst, Ordering::SeqCst).is_err() {
            return;
        }
        info!("Received shutdown signal (SIGTERM/SIGINT) - cleaning up...");
        
        // In signal handler context, we can't easily access Tauri app state
        // The atomic flag prevents duplicate calls, and the OS will clean up windows
        // For tray icon cleanup, we rely on the main window's close handler
        // which calls remove_tray_by_id before exit
        std::process::exit(0);
    }).expect("Error setting signal handler");
}

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

#[derive(Parser, Debug)]
#[command(name = "aic-app")]
#[command(about = "AI Consumption Tracker Desktop Application")]
struct Args {
    /// Enable debug logging (verbose output)
    #[arg(long)]
    debug: bool,
}

#[tokio::main]
async fn main() {
    // Parse command line arguments
    let args = Args::parse();
    
    // Set up file logging
    let app_data_dir = std::env::var("LOCALAPPDATA")
        .map(|p| std::path::PathBuf::from(p).join("ai-consumption-tracker"))
        .unwrap_or_else(|_| std::path::PathBuf::from(".ai-consumption-tracker"));
    let log_dir = app_data_dir.join("logs");
    let _ = std::fs::create_dir_all(&log_dir);
    
    let log_file_path = log_dir.join("app.log");
    let log_file = std::fs::OpenOptions::new()
        .create(true)
        .append(true)
        .open(&log_file_path)
        .expect("Failed to open log file");
    
    let file_layer = tracing_subscriber::fmt::layer()
        .with_writer(std::sync::Mutex::new(log_file))
        .with_ansi(false)
        .with_target(false);
    
    let env_filter = tracing_subscriber::EnvFilter::try_from_default_env()
        .unwrap_or_else(|_| tracing_subscriber::EnvFilter::new(if args.debug { "debug" } else { "info" }));
    
    tracing_subscriber::registry()
        .with(env_filter)
        .with(tracing_subscriber::fmt::layer().with_writer(std::io::stdout))
        .with(file_layer)
        .init();
    
    info!("Log file: {:?}", log_file_path);
    
    // Clean up old log files (keep last 30 days)
    clean_old_logs(&log_dir);
    
    info!("Starting AI Consumption Tracker application");

    // Set up signal handlers for clean shutdown (Ctrl+C, SIGINT, SIGTERM)
    setup_signal_handlers();

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
            preloaded_settings: Arc::new(Mutex::new(None)),
            data_is_live: Arc::new(Mutex::new(false)),
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
            save_provider_configs,
            preload_settings_data,
            get_preloaded_settings_data,
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
            get_agent_port_cmd,
            get_all_ui_data,
            stream_ui_data,
            get_agent_status,
            get_agent_status_details,
            get_all_providers_from_agent,
            get_historical_usage_from_agent,
            get_raw_responses_from_agent,
            // Data status command
            get_data_status,
            set_data_live,
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
                            cleanup_and_exit(app);
                        }
                        _ => {}
                    }
                })
                .build(app)?;

            // Handle tray icon click events
            tray.on_tray_icon_event(|tray, event| {
                if let TrayIconEvent::Click { button: tauri::tray::MouseButton::Left, position, .. } = event {
                    // Show window on left click near tray icon
                    if let Some(window) = tray.app_handle().get_webview_window("main") {
                        // Get window size
                        if let Ok(window_size) = window.inner_size() {
                            // Get monitor info for taskbar calculation
                            let window_width = window_size.width as f64;
                            let window_height = window_size.height as f64;
                            
                            // Get monitor dimensions to account for taskbar
                            let mut work_area_x = 0.0;
                            let mut work_area_y = 0.0;
                            let mut work_area_w = f64::MAX;
                            let mut work_area_h = f64::MAX;
                            
                            // Get monitor for the click position
                            if let Ok(Some(monitor)) = window.monitor_from_point(position.x, position.y) {
                                let work_area = monitor.work_area();
                                work_area_x = work_area.position.x as f64;
                                work_area_y = work_area.position.y as f64;
                                work_area_w = work_area.size.width as f64;
                                work_area_h = work_area.size.height as f64;
                            } else if let Ok(Some(monitor)) = window.primary_monitor() {
                                // Fallback to primary monitor
                                let work_area = monitor.work_area();
                                work_area_x = work_area.position.x as f64;
                                work_area_y = work_area.position.y as f64;
                                work_area_w = work_area.size.width as f64;
                                work_area_h = work_area.size.height as f64;
                            }
                            
                            // 1. Initial preferred position: centered horizontally above the tray icon
                            // position.x is the click position. We want the window center to be near it.
                            let mut x = position.x - (window_width / 2.0); 
                            let mut y = position.y - window_height - 5.0; 
                            
                            // 2. Adjust if it falls outside work area (top taskbar case)
                            if y < work_area_y {
                                y = position.y + 5.0; // Show below tray
                            }
                            
                            // 3. STRICT CLAMPING to work area (ensures it never covers taskbar)
                            // Add a standard 12px margin from any edge for a flyout look
                            let margin = 12.0;
                            
                            x = x.max(work_area_x + margin)
                                 .min(work_area_x + work_area_w - window_width - margin);
                            y = y.max(work_area_y + margin)
                                 .min(work_area_y + work_area_h - window_height - margin);
                            
                            info!("Positioning window at x={}, y={} (tray click: {:?}, monitor work_area: [{}, {}, {}x{}])", 
                                  x, y, position, work_area_x, work_area_y, work_area_w, work_area_h);
                            
                            let _ = window.set_position(tauri::Position::Physical(tauri::PhysicalPosition { 
                                x: x as i32, 
                                y: y as i32 
                            }));
                        }
                        
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
                // Position window near system tray (bottom-right) on first startup
                if let Ok(Some(monitor)) = window.primary_monitor() {
                    // Use configured window size from tauri.conf.json
                    let window_width = 480.0;
                    let window_height = 500.0;
                    
                    // Get the work area (available area excluding taskbar/dock)
                    let work_area = monitor.work_area();
                    let work_x = work_area.position.x as f64;
                    let work_y = work_area.position.y as f64;
                    let work_width = work_area.size.width as f64;
                    let work_height = work_area.size.height as f64;
                    
                    // Position window in bottom-right corner of the work area
                    // Add a consistent 12px margin
                    let margin = 12.0;
                    let x = work_x + work_width - window_width - margin;
                    let y = work_y + work_height - window_height - margin;
                    
                    info!("Startup positioning: x={}, y={} (work_area: [{}, {}, {}x{}])", 
                          x, y, work_x, work_y, work_width, work_height);
                    
                    let _ = window.set_position(tauri::Position::Physical(tauri::PhysicalPosition { 
                        x: x as i32, 
                        y: y as i32 
                    }));
                }
                
                window.show()?;
                window.set_focus()?;
                
                // Set window title with version number
                let version = env!("CARGO_PKG_VERSION");
                window.set_title(&format!("AI Consumption Tracker v{}", version))?;
                
                // Handle window close event - cleanup and exit
                let window_clone = window.clone();
                let main_closing = Arc::new(AtomicBool::new(false));
                window.on_window_event(move |event| {
                    if let tauri::WindowEvent::CloseRequested { api, .. } = event {
                        if main_closing.compare_exchange(false, true, Ordering::SeqCst, Ordering::SeqCst).is_err() {
                            return; // Already closing
                        }
                        info!("Main window close requested - cleaning up");
                        api.prevent_close();
                        // Remove tray icon and exit directly
                        let app_handle = window_clone.app_handle().clone();
                        let _ = app_handle.remove_tray_by_id("main");
                        app_handle.exit(0);
                    }
                });
                
                info!("Main window shown successfully");
            } else {
                warn!("Main window not found!");
            }

            // Add close handler for settings window
            if let Some(settings_window) = app.get_webview_window("settings") {
                let settings_window_clone = settings_window.clone();
                let settings_closing = Arc::new(AtomicBool::new(false));
                settings_window.on_window_event(move |event| {
                    if let tauri::WindowEvent::CloseRequested { api, .. } = event {
                        if settings_closing.compare_exchange(false, true, Ordering::SeqCst, Ordering::SeqCst).is_err() {
                            return; // Already closing
                        }
                        info!("Settings window close requested");
                        api.prevent_close();
                        let _ = settings_window_clone.close();
                    }
                });
                info!("Settings window close handler installed");
            }

            // Add close handler for info window
            if let Some(info_window) = app.get_webview_window("info") {
                let info_window_clone = info_window.clone();
                let info_closing = Arc::new(AtomicBool::new(false));
                info_window.on_window_event(move |event| {
                    if let tauri::WindowEvent::CloseRequested { api, .. } = event {
                        if info_closing.compare_exchange(false, true, Ordering::SeqCst, Ordering::SeqCst).is_err() {
                            return; // Already closing
                        }
                        info!("Info window close requested");
                        api.prevent_close();
                        let _ = info_window_clone.close();
                    }
                });
                info!("Info window close handler installed");
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

            // Auto-start agent if not running
            let app_handle = app.handle().clone();
            let agent_process = app.state::<AppState>().agent_process.clone();
            tokio::spawn(async move {
                log_timing("App initialized, starting agent check");
                // No delay - check immediately
                info!("[AUTO-START] Checking if agent is running on startup...");
                
                let is_running = match check_agent_status().await {
                    Ok(running) => {
                        info!("[AUTO-START] Agent status check result: {}", running);
                        running
                    }
                    Err(e) => {
                        error!("[AUTO-START] Failed to check agent status: {}", e);
                        false
                    }
                };

                if !is_running {
                    log_timing("Agent not running, starting agent...");
                    info!("[AUTO-START] Agent not running, starting automatically...");
                    match start_agent_internal(&app_handle, agent_process).await {
                        Ok(started) => {
                            if started {
                                log_timing("Agent started successfully");
                                info!("[AUTO-START] Agent started successfully");
                                
                                // Warm-up: pre-fetch usage data and push to frontend via event
                                info!("[WARM-UP] Pre-fetching usage data and pushing to UI...");
                                let client = reqwest::Client::new();
                                let port = get_agent_port().await;
                                if let Ok(response) = client
                                    .get(format!("http://localhost:{}/api/providers/usage", port))
                                    .timeout(Duration::from_secs(5))
                                    .send()
                                    .await
                                {
                                    if response.status().is_success() {
                                        if let Ok(usage) = response.json::<Vec<ProviderUsage>>().await {
                                            info!("[WARM-UP] Pushing {} providers to frontend", usage.len());
                                            let _ = app_handle.emit("ui-data-usage", &usage);
                                        }
                                    }
                                }
                            } else {
                                warn!("[AUTO-START] Agent failed to start (returned false)");
                            }
                        }
                        Err(e) => {
                            error!("[AUTO-START] Failed to start agent: {}", e);
                        }
                    }
                } else {
                    log_timing("Agent already running");
                    info!("[AUTO-START] Agent is already running, no need to start");
                    
                    // Warm-up: pre-fetch data and push to frontend via event
                    info!("[WARM-UP] Pre-fetching usage data (agent already running)...");
                    let client = reqwest::Client::new();
                    let port = get_agent_port().await;
                    if let Ok(response) = client
                        .get(format!("http://localhost:{}/api/providers/usage", port))
                        .timeout(Duration::from_secs(5))
                        .send()
                        .await
                    {
                        if response.status().is_success() {
                            if let Ok(usage) = response.json::<Vec<ProviderUsage>>().await {
                                info!("[WARM-UP] Pushing {} providers to frontend (already running)", usage.len());
                                let _ = app_handle.emit("ui-data-usage", &usage);
                            }
                        }
                    }
                }

                // Preload settings data for settings window
                info!("[WARM-UP] Preloading settings data for settings window...");
                let state = app_handle.state::<AppState>();
                let preloaded = state.preloaded_settings.clone();
                
                let providers_future = async {
                    let port = get_agent_port().await;
                    let agent_url = format!("http://localhost:{}/api/providers/discovered", port);
                    match reqwest::get(&agent_url).await {
                        Ok(response) if response.status().is_success() => {
                            match response.json::<Vec<ProviderConfig>>().await {
                                Ok(providers) => Some(providers),
                                Err(_) => None,
                            }
                        }
                        _ => None,
                    }
                };
                
                let usage_future = async {
                    let port = get_agent_port().await;
                    let agent_url = format!("http://localhost:{}/api/providers/usage", port);
                    match reqwest::get(&agent_url).await {
                        Ok(response) if response.status().is_success() => {
                            match response.json::<Vec<ProviderUsage>>().await {
                                Ok(usage) => Some(usage),
                                Err(_) => None,
                            }
                        }
                        _ => None,
                    }
                };
                
                let agent_info_future = async {
                    let port = get_agent_port().await;
                    let agent_url = format!("http://localhost:{}/api/agent/info", port);
                    match reqwest::get(&agent_url).await {
                        Ok(response) if response.status().is_success() => {
                            match response.json::<AgentInfo>().await {
                                Ok(info) => Some(info),
                                Err(_) => None,
                            }
                        }
                        _ => None,
                    }
                };
                
                let (providers, usage, agent_info) = tokio::join!(providers_future, usage_future, agent_info_future);
                
                let mut preloaded_guard = preloaded.lock().await;
                *preloaded_guard = Some(PreloadedSettings {
                    providers: providers.unwrap_or_default(),
                    usage: usage.unwrap_or_default(),
                    agent_info,
                });
                info!("[WARM-UP] Settings data preloaded successfully");
            });

            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("error while running tauri application")
}