use anyhow::Result;
use axum::{
    extract::{Path, Query, State},
    http::StatusCode,
    response::Json,
    routing::{delete, get, post, put},
    Router,
};
use chrono::{DateTime, Utc};
use clap::Parser;
use serde::{Deserialize, Serialize};
use serde_json;
use libsql::Builder;
use std::sync::Arc;
use std::time::Instant;
use tokio::sync::RwLock;
use tracing::{info, error, debug, warn};
use tracing_subscriber::{layer::SubscriberExt, util::SubscriberInitExt};
use uuid::Uuid;

// Import ProviderUsage from aic_core to ensure API compatibility
use aic_core::ProviderUsage;
use aic_core::github_auth::GitHubAuthService;

mod config;
mod database;

use config::AgentConfig;

#[derive(Parser, Debug)]
#[command(author, version, about, long_about = None)]
struct Args {
    #[arg(short, long, default_value_t = 8080)]
    port: u16,

    #[arg(long)]
    db_url: Option<String>,

    #[arg(long, default_value_t = 5)]
    refresh_interval_minutes: u64,

    /// Enable debug logging (verbose output)
    #[arg(long)]
    debug: bool,
}

#[derive(Clone)]
struct AppState {
    db: Arc<database::Database>,
    config: Arc<RwLock<AgentConfig>>,
    provider_manager: Arc<aic_core::config::ProviderManager>,
    github_auth_service: Arc<GitHubAuthService>,
    start_time: Instant,
}

// UsageResponse is replaced with ProviderUsage from aic_core for API compatibility

// Use database::HistoricalUsageRecord directly
use database::HistoricalUsageRecord;

#[tokio::main]
async fn main() -> Result<()> {
    let args = Args::parse();

    // Set log level based on --debug flag
    let log_level = if args.debug { "debug" } else { "info" };
    let env_filter = tracing_subscriber::EnvFilter::try_from_default_env()
        .unwrap_or_else(|_| tracing_subscriber::EnvFilter::new(log_level));
    tracing_subscriber::registry()
        .with(env_filter)
        .with(tracing_subscriber::fmt::layer())
        .init();

    info!("Starting AI Consumption Tracker Agent v{}", env!("CARGO_PKG_VERSION"));

    let database_path = args.db_url.unwrap_or_else(|| {
        "./agent.db".to_string()
    });

    info!("Using database: {}", database_path);

    let database = database::Database::new(std::path::Path::new(&database_path)).await?;
    info!("Database initialized successfully");

    // Discover providers on startup
    info!("Discovering providers...");
    let discovered_providers = config::discover_all_providers().await;
    info!("Discovered {} providers on startup", discovered_providers.len());
    for provider in &discovered_providers {
        info!("  - {} ({})", provider.provider_id, provider.auth_source);
    }

    let config = Arc::new(RwLock::new(AgentConfig {
        refresh_interval_minutes: args.refresh_interval_minutes,
        auto_refresh_enabled: true,
        discovered_providers,
    }));

    // Create HTTP client
    let client = reqwest::Client::new();
    info!("HTTP client created");

    // Create provider manager
    let provider_manager = Arc::new(aic_core::config::ProviderManager::new(client.clone()));
    info!("Provider manager created");

    // Create GitHub auth service
    let github_auth_service = Arc::new(GitHubAuthService::new(client.clone()));
    info!("GitHub auth service created");

    let state = AppState {
        db: Arc::new(database),
        config,
        provider_manager,
        github_auth_service,
        start_time: Instant::now(),
    };
    info!("App state initialized with uptime tracking");

    let scheduler_handle = start_scheduler(state.clone()).await?;
    info!("Scheduler started");

    let app = Router::new()
        .route("/health", get(health_check))
        .route("/debug/info", get(debug_info))
        .route("/debug/config", get(debug_config))
        .route("/api/providers/usage", get(get_current_usage))
        .route("/api/providers/usage/refresh", post(trigger_refresh))
        .route("/api/providers/:id/usage", get(get_provider_usage))
        .route("/api/providers/discovered", get(get_discovered_providers))
        .route("/api/history", get(get_historical_usage))
        .route("/api/raw_responses", get(get_raw_responses))
        .route("/api/config", get(get_config))
        .route("/api/config", post(update_config))
        .route("/api/discover", post(trigger_discovery))
        .route("/api/config/providers", post(save_all_providers))
        .route("/api/providers/:id", put(save_provider))
        .route("/api/providers/:id", delete(remove_provider))
        .route("/api/auth/github/device", post(initiate_github_device_flow))
        .route("/api/auth/github/poll", post(poll_github_token))
        .route("/api/auth/github/status", get(get_github_auth_status))
        .route("/api/auth/github/logout", post(logout_github))
        .with_state(state);
    info!("Routes registered");

    let bind_address = format!("127.0.0.1:{}", args.port);
    info!("Attempting to bind to {}", bind_address);
    let listener = tokio::net::TcpListener::bind(&bind_address).await?;
    info!("Successfully bound to {}", bind_address);

    axum::serve(listener, app).await?;

    if let Some(handle) = scheduler_handle {
        handle.abort();
    }

    Ok(())
}

// create_tables removed

async fn start_scheduler(
    state: AppState,
) -> Result<Option<tokio::task::JoinHandle<()>>> {
    use std::time::Duration;
    use tokio::time::interval;

    let db = state.db.clone();
    let config = state.config.clone();
    let provider_manager = state.provider_manager.clone();

    let sched = tokio::spawn(async move {
        let mut tick = interval(Duration::from_secs(60));

        loop {
            tick.tick().await;

            let config_read = config.read().await;

            if config_read.auto_refresh_enabled {
                debug!("Auto-refresh enabled, checking if refresh is due");

                let interval_secs = config_read.refresh_interval_minutes * 60;

                let latest_records = db.get_latest_usage_records(1).await;
                let last_refresh = latest_records.first().map(|r| {
                    DateTime::parse_from_rfc3339(&r.timestamp)
                        .unwrap_or_else(|_| Utc::now().into())
                        .with_timezone(&Utc)
                });

                let should_refresh = match last_refresh {
                    Some(ts) => {
                        let now = Utc::now();
                        let elapsed = (now - ts).num_seconds();
                        elapsed as u64 >= interval_secs
                    }
                    None => true,
                };

                if should_refresh {
                    info!("Triggering scheduled refresh and cleanup");
                    if let Err(e) = refresh_and_store(&db, &provider_manager).await {
                        error!("Refresh failed: {}", e);
                    }
                    
                    // Run database cleanup
                    if let Err(e) = db.cleanup_raw_responses().await {
                        error!("Raw response cleanup failed: {}", e);
                    }
                    
                    // Also run historical cleanup (30 days retention)
                    if let Err(e) = db.cleanup_old_records(30).await {
                        error!("Historical cleanup failed: {}", e);
                    }
                }
            } else {
                debug!("Auto-refresh disabled");
            }
        }
    });

    Ok(Some(sched))
}

// fetch_latest_record removed

async fn health_check(State(state): State<AppState>) -> Json<serde_json::Value> {
    use serde_json::json;
    
    let uptime_secs = state.start_time.elapsed().as_secs();
    let github_authenticated = state.github_auth_service.is_authenticated();
    
    debug!("API: GET /health - Health check (uptime: {}s, github_auth: {})", uptime_secs, github_authenticated);

    Json(json!({
        "status": "ok",
        "version": env!("CARGO_PKG_VERSION"),
        "uptime_seconds": uptime_secs,
        "github_authenticated": github_authenticated,
        "timestamp": chrono::Utc::now().to_rfc3339(),
    }))
}

async fn debug_info() -> Json<serde_json::Value> {
    use serde_json::json;
    
    info!("API: GET /debug/info - Debug info request");

    Json(json!({
        "version": env!("CARGO_PKG_VERSION"),
        "profile": if cfg!(debug_assertions) { "debug" } else { "release" },
        "timestamp": chrono::Utc::now().to_rfc3339(),
    }))
}

async fn debug_config(State(state): State<AppState>) -> Json<serde_json::Value> {
    use serde_json::json;
    
    info!("API: GET /debug/config - Debug config request");
    
    let config = state.config.read().await;
    let discovered = config.discovered_providers.clone();
    
    Json(json!({
        "refresh_interval_minutes": config.refresh_interval_minutes,
        "auto_refresh_enabled": config.auto_refresh_enabled,
        "discovered_providers_count": discovered.len(),
        "discovered_providers": discovered.iter().map(|p| &p.provider_id).collect::<Vec<_>>(),
    }))
}

async fn get_current_usage(
    State(state): State<AppState>,
) -> Result<Json<Vec<ProviderUsage>>, StatusCode> {
    info!("API: GET /api/providers/usage - Fetching current usage");
    
    // Use cached provider manager from state - returns cached data immediately if available
    let usages = state.provider_manager.get_all_usage(false).await;
    
    info!("API: Returning {} usage records", usages.len());
    
    // Log details of each provider
    for usage in &usages {
        info!(
            "  Provider: {} ({}), Available: {}, Cost: {:.2} / {:.2}, Usage%: {:.1}",
            usage.provider_name,
            usage.provider_id,
            usage.is_available,
            usage.cost_used,
            usage.cost_limit,
            usage.usage_percentage
        );
    }
    
    // Debug: Serialize to JSON and log first 500 chars
    match serde_json::to_string(&usages) {
        Ok(json) => {
            let preview = if json.len() > 500 { &json[..500] } else { &json };
            debug!("API: JSON response preview: {}", preview);
        }
        Err(e) => {
            error!("API: Failed to serialize usages: {}", e);
        }
    }

    Ok(Json(usages))
}

async fn trigger_refresh(
    State(state): State<AppState>,
) -> Result<Json<Vec<ProviderUsage>>, StatusCode> {
    info!("API: POST /api/providers/usage/refresh - Refreshing usage data");
    
    let usages = state.provider_manager.get_all_usage(true).await;
    info!("API: Fetched {} provider records", usages.len());
    
    if let Err(e) = refresh_and_store(&state.db, &state.provider_manager).await {
        error!("API: Manual refresh failed to store: {}", e);
    }

    Ok(Json(usages))
}

async fn get_provider_usage(
    State(state): State<AppState>,
    Path(provider_id): Path<String>,
) -> Result<Json<ProviderUsage>, (StatusCode, &'static str)> {
    info!("API: GET /api/providers/{}/usage - Fetching specific provider", provider_id);
    
    // Use cached provider manager from state
    let usages = state.provider_manager.get_all_usage(false).await;
    info!("API: Searching through {} providers for {}", usages.len(), provider_id);

    let usage = usages
        .into_iter()
        .find(|u| u.provider_id.eq_ignore_ascii_case(&provider_id))
        .ok_or_else(|| {
            warn!("API: Provider '{}' not found", provider_id);
            (StatusCode::NOT_FOUND, "Provider not found")
        })?;

    info!("API: Returning usage for provider '{}'", provider_id);
    Ok(Json(usage))
}

async fn get_historical_usage(
    State(state): State<AppState>,
    Query(params): Query<HistoryQuery>,
) -> Result<Json<Vec<HistoricalUsageRecord>>, StatusCode> {
    let db = &state.db;

    let records = if let Some(provider_id) = params.provider_id {
        db.get_usage_records_by_provider(&provider_id).await
    } else if let (Some(start), Some(end)) = (params.start_date, params.end_date) {
        db.get_usage_records_by_time_range(start, end).await
    } else {
        db.get_latest_usage_records(params.limit.unwrap_or(100)).await
    };

    Ok(Json(records))
}

// build_historical_query removed

async fn get_raw_responses(
    State(state): State<AppState>,
    Query(params): Query<HistoryQuery>,
) -> Result<Json<Vec<database::RawResponse>>, StatusCode> {
    let db = &state.db;
    let logs = db.get_raw_responses(params.provider_id, params.limit.unwrap_or(20)).await;
    Ok(Json(logs))
}

#[derive(Debug, Deserialize)]
struct HistoryQuery {
    provider_id: Option<String>,
    start_date: Option<DateTime<Utc>>,
    end_date: Option<DateTime<Utc>>,
    limit: Option<usize>,
}

async fn get_config(
    State(state): State<AppState>,
) -> Json<AgentConfig> {
    let config = state.config.read().await;
    Json(config.clone())
}

async fn update_config(
    State(state): State<AppState>,
    Json(new_config): Json<AgentConfig>,
) -> Json<AgentConfig> {
    let mut config = state.config.write().await;

    config.refresh_interval_minutes = new_config.refresh_interval_minutes;
    config.auto_refresh_enabled = new_config.auto_refresh_enabled;

    info!("Configuration updated: {:?}", config);

    Json(config.clone())
}

async fn trigger_discovery(
    State(state): State<AppState>,
) -> Json<Vec<aic_core::ProviderConfig>> {
    info!("API: POST /api/discover - Triggering provider discovery");
    
    // Re-run provider discovery
    let discovered = crate::config::discover_all_providers().await;
    
    // Update the in-memory discovered providers
    {
        let mut config = state.config.write().await;
        config.discovered_providers = discovered.clone();
    }
    
    info!("API: Discovery complete. Found {} providers", discovered.len());
    Json(discovered)
}

async fn save_all_providers(
    State(state): State<AppState>,
    Json(providers): Json<Vec<aic_core::ProviderConfig>>,
) -> Result<Json<Vec<aic_core::ProviderConfig>>, StatusCode> {
    info!("API: POST /api/config/providers - Saving all provider configurations (count: {})", providers.len());
    
    // Use the agent's config to save all providers
    let config_loader = aic_core::ConfigLoader::new(reqwest::Client::new());
    
    // Save all configs to auth.json
    if let Err(e) = config_loader.save_config(&providers).await {
        error!("Failed to save all provider configs: {}", e);
        return Err(StatusCode::INTERNAL_SERVER_ERROR);
    }
    
    // Update the in-memory discovered providers
    {
        let mut config = state.config.write().await;
        config.discovered_providers = providers.clone();
    }
    
    info!("Successfully saved all {} providers", providers.len());
    Ok(Json(providers))
}

async fn save_provider(
    State(state): State<AppState>,
    Path(provider_id): Path<String>,
    Json(provider_config): Json<aic_core::ProviderConfig>,
) -> Result<Json<Vec<aic_core::ProviderConfig>>, StatusCode> {
    info!("API: PUT /api/providers/{} - Saving provider configuration", provider_id);
    
    // Use the agent's config to save the provider
    let config_loader = aic_core::ConfigLoader::new(reqwest::Client::new());
    
    // Load existing configs
    let mut configs = config_loader.load_primary_config().await;
    
    // Update or add the provider
    if let Some(existing) = configs.iter_mut().find(|c| c.provider_id == provider_id) {
        *existing = provider_config;
        info!("Updated existing provider: {}", provider_id);
    } else {
        configs.push(provider_config);
        info!("Added new provider: {}", provider_id);
    }
    
    // Save back to auth.json
    if let Err(e) = config_loader.save_config(&configs).await {
        error!("Failed to save provider config: {}", e);
        return Err(StatusCode::INTERNAL_SERVER_ERROR);
    }
    
    // Update the in-memory discovered providers
    {
        let mut config = state.config.write().await;
        config.discovered_providers = configs.clone();
    }
    
    info!("Successfully saved provider {}. Total providers: {}", provider_id, configs.len());
    Ok(Json(configs))
}

async fn remove_provider(
    State(state): State<AppState>,
    Path(provider_id): Path<String>,
) -> Result<Json<Vec<aic_core::ProviderConfig>>, StatusCode> {
    info!("API: DELETE /api/providers/{} - Removing provider configuration", provider_id);
    
    // Use the agent's config to remove the provider
    let config_loader = aic_core::ConfigLoader::new(reqwest::Client::new());
    
    // Load existing configs
    let mut configs = config_loader.load_primary_config().await;
    
    // Remove the provider
    let initial_count = configs.len();
    configs.retain(|c| c.provider_id != provider_id);
    
    if configs.len() < initial_count {
        info!("Removed provider: {}", provider_id);
        
        // Save back to auth.json
        if let Err(e) = config_loader.save_config(&configs).await {
            error!("Failed to save config after removal: {}", e);
            return Err(StatusCode::INTERNAL_SERVER_ERROR);
        }
        
        // Update the in-memory discovered providers
        {
            let mut config = state.config.write().await;
            config.discovered_providers = configs.clone();
        }
        
        info!("Successfully removed provider {}. Remaining providers: {}", provider_id, configs.len());
    } else {
        info!("Provider {} not found, nothing to remove", provider_id);
    }
    
    Ok(Json(configs))
}

async fn get_discovered_providers(
    State(state): State<AppState>,
) -> Json<Vec<aic_core::ProviderConfig>> {
    info!("API: GET /api/providers/discovered - Fetching discovered providers");
    
    let config = state.config.read().await;
    let providers = config.discovered_providers.clone();
    
    info!("API: Returning {} discovered providers", providers.len());
    
    // Log each provider
    for provider in &providers {
        let key_status = if provider.api_key.is_empty() { "no key" } else { "has key" };
        info!(
            "  Provider: {} ({}), Source: {}, {}",
            provider.provider_id,
            provider.config_type,
            provider.auth_source,
            key_status
        );
    }
    
    Json(providers)
}

async fn refresh_and_store(
    db: &database::Database,
    provider_manager: &aic_core::config::ProviderManager,
) -> Result<()> {
    let usages = provider_manager.get_all_usage(true).await;
    let now = Utc::now();

    for u in &usages {
        if u.is_available && u.cost_used >= 0.0 {
            // Delta logic:
            // 1. Get last record for this provider
            let last_record = db.get_latest_usage_for_provider(&u.provider_id).await;
            
            let should_store = match last_record {
                Some(ref last) => {
                    let usage_changed = (u.cost_used - last.usage).abs() > 0.000001;
                    
                    let last_ts = DateTime::parse_from_rfc3339(&last.timestamp)
                        .unwrap_or_else(|_| Utc::now().into())
                        .with_timezone(&Utc);
                    let heartbeat_due = (now - last_ts).num_hours() >= 1;
                    
                    usage_changed || heartbeat_due
                }
                None => true, // Always store first record
            };

            if should_store {
                let timestamp = now.to_rfc3339();

                let record = database::HistoricalUsageRecord {
                    id: "".to_string(), // Database will generate an ID
                    provider_id: u.provider_id.clone(),
                    provider_name: u.provider_name.clone(),
                    usage: u.cost_used,
                    limit: Some(u.cost_limit),
                    usage_unit: u.usage_unit.clone(),
                    is_quota_based: u.is_quota_based,
                    timestamp,
                    next_reset_time: u.next_reset_time.as_ref().map(|dt| dt.to_rfc3339()),
                };

                if let Err(e) = db.insert_usage_record(&record).await {
                    error!("Failed to insert usage record for {}: {}", u.provider_id, e);
                } else {
                    debug!("Stored usage record for {} (usage: {})", u.provider_id, u.cost_used);
                    
                    // Also store raw response if available
                    if let Some(ref raw) = u.raw_response {
                        if let Err(e) = db.insert_raw_response(&u.provider_id, raw).await {
                            error!("Failed to store raw response for {}: {}", u.provider_id, e);
                        }
                    }
                }
            } else {
                debug!("Skipping storage for {} (no change and heartbeat not due)", u.provider_id);
            }
        }
    }

    Ok(())
}

// GitHub OAuth Device Flow handlers

async fn initiate_github_device_flow(
    State(state): State<AppState>,
) -> Json<serde_json::Value> {
    info!("API: POST /api/auth/github/device - Initiating GitHub device flow");

    match state.github_auth_service.initiate_device_flow().await {
        Ok(response) => {
            info!("Device flow initiated. User code: {}", response.user_code);
            Json(serde_json::json!({
                "success": true,
                "device_code": response.device_code,
                "user_code": response.user_code,
                "verification_uri": response.verification_uri,
                "expires_in": response.expires_in,
                "interval": response.interval
            }))
        }
        Err(e) => {
            error!("Failed to initiate device flow: {}", e);
            Json(serde_json::json!({
                "success": false,
                "error": e
            }))
        }
    }
}

#[derive(Debug, Deserialize)]
struct PollTokenRequest {
    device_code: String,
    interval: i64,
}

async fn poll_github_token(
    State(state): State<AppState>,
    Json(request): Json<PollTokenRequest>,
) -> Json<serde_json::Value> {
    info!("API: POST /api/auth/github/poll - Polling for GitHub token");

    match state.github_auth_service.poll_for_token(&request.device_code).await {
        aic_core::github_auth::TokenPollResult::Token(token) => {
            info!("GitHub token received successfully");
            Json(serde_json::json!({
                "success": true,
                "token": token
            }))
        }
        aic_core::github_auth::TokenPollResult::Pending => {
            Json(serde_json::json!({
                "success": false,
                "status": "pending"
            }))
        }
        aic_core::github_auth::TokenPollResult::SlowDown => {
            warn!("GitHub poll received slow_down, need to increase interval");
            Json(serde_json::json!({
                "success": false,
                "status": "slow_down"
            }))
        }
        aic_core::github_auth::TokenPollResult::Expired => {
            error!("GitHub token expired");
            Json(serde_json::json!({
                "success": false,
                "error": "Token expired"
            }))
        }
        aic_core::github_auth::TokenPollResult::AccessDenied => {
            error!("GitHub access denied by user");
            Json(serde_json::json!({
                "success": false,
                "error": "Access denied"
            }))
        }
        aic_core::github_auth::TokenPollResult::Error(msg) => {
            error!("GitHub poll error: {}", msg);
            Json(serde_json::json!({
                "success": false,
                "error": msg
            }))
        }
    }
}

async fn get_github_auth_status(
    State(state): State<AppState>,
) -> Json<serde_json::Value> {
    info!("API: GET /api/auth/github/status - Getting GitHub auth status");

    let is_authenticated = state.github_auth_service.is_authenticated();
    let username = if is_authenticated {
        state.github_auth_service.get_username().await
    } else {
        None
    };

    Json(serde_json::json!({
        "is_authenticated": is_authenticated,
        "username": username
    }))
}

async fn logout_github(
    State(state): State<AppState>,
) -> Json<serde_json::Value> {
    info!("API: POST /api/auth/github/logout - Logging out from GitHub");

    state.github_auth_service.logout();

    Json(serde_json::json!({
        "success": true
    }))
}
