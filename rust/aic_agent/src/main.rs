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
use tokio::sync::RwLock;
use tracing::{info, error, debug, warn};
use tracing_subscriber::{layer::SubscriberExt, util::SubscriberInitExt};
use uuid::Uuid;

// Import ProviderUsage from aic_core to ensure API compatibility
use aic_core::ProviderUsage;

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

    #[arg(long, default_value = "info")]
    log_level: String,
}

#[derive(Clone)]
struct AppState {
    db: Arc<libsql::Database>,
    config: Arc<RwLock<AgentConfig>>,
    provider_manager: Arc<aic_core::config::ProviderManager>,
}

// UsageResponse is replaced with ProviderUsage from aic_core for API compatibility

#[derive(Debug, Clone, Serialize, Deserialize)]
struct HistoricalUsageRecord {
    id: String,
    provider_id: String,
    provider_name: String,
    usage: f64,
    limit: Option<f64>,
    usage_unit: String,
    is_quota_based: bool,
    timestamp: DateTime<Utc>,
}

#[tokio::main]
async fn main() -> Result<()> {
    let args = Args::parse();

    let env_filter = tracing_subscriber::EnvFilter::try_from_default_env()
        .unwrap_or_else(|_| tracing_subscriber::EnvFilter::new(&args.log_level));
    tracing_subscriber::registry()
        .with(env_filter)
        .with(tracing_subscriber::fmt::layer())
        .init();

    info!("Starting AI Consumption Tracker Agent v{}", env!("CARGO_PKG_VERSION"));

    let db_path = args.db_url.unwrap_or_else(|| {
        "./agent.db".to_string()
    });

    info!("Using database: {}", db_path);

    let db = Builder::new_local(&db_path).build().await?;

    let conn = db.connect()?;
    create_tables(&conn).await?;

    // Discover providers on startup
    info!("Discovering providers...");
    let discovered_providers = config::discover_all_providers().await;
    info!("Discovered {} providers on startup", discovered_providers.len());

    let config = Arc::new(RwLock::new(AgentConfig {
        refresh_interval_minutes: args.refresh_interval_minutes,
        auto_refresh_enabled: true,
        discovered_providers,
    }));

    // Create provider manager once and share it across requests
    let client = reqwest::Client::new();
    let provider_manager = Arc::new(aic_core::config::ProviderManager::new(client));

    let state = AppState {
        db: Arc::new(db),
        config,
        provider_manager,
    };

    let scheduler_handle = start_scheduler(state.clone()).await?;

    let app = Router::new()
        .route("/health", get(health_check))
        .route("/debug/info", get(debug_info))
        .route("/debug/config", get(debug_config))
        .route("/api/providers/usage", get(get_current_usage))
        .route("/api/providers/usage/refresh", post(trigger_refresh))
        .route("/api/providers/:id/usage", get(get_provider_usage))
        .route("/api/providers/discovered", get(get_discovered_providers))
        .route("/api/history", get(get_historical_usage))
        .route("/api/config", get(get_config))
        .route("/api/config", post(update_config))
        .route("/api/discover", post(trigger_discovery))
        .route("/api/config/providers", post(save_all_providers))
        .route("/api/providers/:id", put(save_provider))
        .route("/api/providers/:id", delete(remove_provider))
        .with_state(state);

    let listener = tokio::net::TcpListener::bind(format!("127.0.0.1:{}", args.port)).await?;
    info!("HTTP server listening on http://127.0.0.1:{}", args.port);

    axum::serve(listener, app).await?;

    if let Some(handle) = scheduler_handle {
        handle.abort();
    }

    Ok(())
}

async fn create_tables(conn: &libsql::Connection) -> Result<()> {
    conn.execute_batch(r#"
        CREATE TABLE IF NOT EXISTS usage_records (
            id TEXT PRIMARY KEY,
            provider_id TEXT NOT NULL,
            provider_name TEXT NOT NULL,
            usage REAL NOT NULL,
            "limit" REAL,
            usage_unit TEXT NOT NULL,
            is_quota_based INTEGER NOT NULL,
            timestamp TEXT NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_provider_id ON usage_records(provider_id);
        CREATE INDEX IF NOT EXISTS idx_timestamp ON usage_records(timestamp);
        CREATE INDEX IF NOT EXISTS idx_provider_timestamp ON usage_records(provider_id, timestamp);
    "#).await?;

    Ok(())
}

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

                let last_refresh = fetch_latest_record(&db).await;
                let should_refresh = match last_refresh {
                    Some(ts) => {
                        let now = Utc::now();
                        let elapsed = (now - ts).num_seconds();
                        elapsed as u64 >= interval_secs
                    }
                    None => true,
                };

                if should_refresh {
                    info!("Triggering scheduled refresh");
                    if let Err(e) = refresh_and_store(&db, &provider_manager).await {
                        error!("Refresh failed: {}", e);
                    }
                }
            } else {
                debug!("Auto-refresh disabled");
            }
        }
    });

    Ok(Some(sched))
}

async fn fetch_latest_record(db: &libsql::Database) -> Option<DateTime<Utc>> {
    let conn = db.connect().ok()?;
    
    let mut rows = conn.query("SELECT MAX(timestamp) FROM usage_records", ()).await.ok()?;

    if let Some(row) = rows.next().await.ok().flatten() {
        let ts: String = row.get(0).ok()?;
        DateTime::parse_from_rfc3339(&ts).map(|dt| dt.with_timezone(&Utc)).ok()
    } else {
        None
    }
}

async fn health_check() -> Json<serde_json::Value> {
    use serde_json::json;
    
    debug!("API: GET /health - Health check");

    Json(json!({
        "status": "ok",
        "version": env!("CARGO_PKG_VERSION"),
        "uptime_seconds": 0,
    }))
}

async fn debug_info() -> Json<serde_json::Value> {
    use serde_json::json;
    
    info!("API: GET /debug/info - Debug info request");

    Json(json!({
        "version": env!("CARGO_PKG_VERSION"),
        "rust_version": env!("RUSTC_VERSION"),
        "target": env!("TARGET"),
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
    
    // Use cached provider manager but force refresh
    info!("API: Fetching fresh data from providers...");
    let usages = state.provider_manager.get_all_usage(true).await;
    info!("API: Fetched {} provider records", usages.len());
    
    let now = Utc::now();

    let db = &state.db;

    for u in &usages {
        if u.is_available && u.cost_used > 0.0 {
            let id = Uuid::new_v4().to_string();
            let timestamp = now.to_rfc3339();

            let conn = db.connect().map_err(|e| {
                error!("Failed to connect to database: {}", e);
                StatusCode::INTERNAL_SERVER_ERROR
            })?;

            let result = conn.execute(
                r#"INSERT OR REPLACE INTO usage_records 
                   (id, provider_id, provider_name, usage, "limit", usage_unit, is_quota_based, timestamp)
                   VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8)"#,
                (
                    id.as_str(),
                    u.provider_id.as_str(),
                    u.provider_name.as_str(),
                    u.cost_used,
                    u.cost_limit,
                    u.usage_unit.as_str(),
                    if u.is_quota_based { 1 } else { 0 },
                    timestamp.as_str(),
                ),
            ).await;

            if let Err(e) = result {
                error!("Failed to insert usage record for {}: {}", u.provider_id, e);
            }
        }
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

    let (sql, params) = build_historical_query(&params);
    
    let conn = db.connect().map_err(|e| {
        error!("Failed to connect to database: {}", e);
        StatusCode::INTERNAL_SERVER_ERROR
    })?;

    let mut rows = conn.query(&sql, params).await.map_err(|e| {
        error!("Failed to fetch historical usage: {}", e);
        StatusCode::INTERNAL_SERVER_ERROR
    })?;

    let mut records = Vec::new();

    while let Some(row) = rows.next().await.map_err(|e| {
        error!("Failed to iterate rows: {}", e);
        StatusCode::INTERNAL_SERVER_ERROR
    })? {
        let id: String = row.get(0).map_err(|_| StatusCode::INTERNAL_SERVER_ERROR)?;
        let provider_id: String = row.get(1).map_err(|_| StatusCode::INTERNAL_SERVER_ERROR)?;
        let provider_name: String = row.get(2).map_err(|_| StatusCode::INTERNAL_SERVER_ERROR)?;
        let usage: f64 = row.get(3).map_err(|_| StatusCode::INTERNAL_SERVER_ERROR)?;
        let limit: Option<f64> = row.get(4).ok();
        let usage_unit: String = row.get(5).map_err(|_| StatusCode::INTERNAL_SERVER_ERROR)?;
        let is_quota_based: bool = row.get::<i64>(6).map_err(|_| StatusCode::INTERNAL_SERVER_ERROR)? == 1;
        let timestamp_str: String = row.get(7).map_err(|_| StatusCode::INTERNAL_SERVER_ERROR)?;
        let timestamp = DateTime::parse_from_rfc3339(&timestamp_str)
            .map(|dt| dt.with_timezone(&Utc))
            .map_err(|_| StatusCode::INTERNAL_SERVER_ERROR)?;
        
        records.push(HistoricalUsageRecord {
            id,
            provider_id,
            provider_name,
            usage,
            limit,
            usage_unit,
            is_quota_based,
            timestamp,
        });
    }

    Ok(Json(records))
}

fn build_historical_query(params: &HistoryQuery) -> (String, (String, String, String)) {
    let mut sql = String::from("SELECT id, provider_id, provider_name, usage, \"limit\", usage_unit, is_quota_based, timestamp FROM usage_records");
    
    let param1 = params.provider_id.clone().unwrap_or_default();
    let param2 = params.start_date.map(|d| d.to_rfc3339()).unwrap_or_default();
    let param3 = params.end_date.map(|d| d.to_rfc3339()).unwrap_or_default();

    if params.provider_id.is_some() || params.start_date.is_some() || params.end_date.is_some() {
        sql.push_str(" WHERE ");
        let mut conditions = Vec::new();
        
        if params.provider_id.is_some() {
            conditions.push("provider_id = ?1");
        }
        
        if params.start_date.is_some() {
            conditions.push("timestamp >= ?2");
        }
        
        if params.end_date.is_some() {
            conditions.push("timestamp <= ?3");
        }
        
        sql.push_str(&conditions.join(" AND "));
    }

    sql.push_str(" ORDER BY timestamp DESC");

    if let Some(limit) = params.limit {
        sql.push_str(&format!(" LIMIT {}", limit));
    }

    (sql, (param1, param2, param3))
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
    db: &libsql::Database,
    provider_manager: &aic_core::config::ProviderManager,
) -> Result<()> {
    let usages = provider_manager.get_all_usage(true).await;

    for u in &usages {
        if u.is_available && u.cost_used > 0.0 {
            let id = Uuid::new_v4().to_string();
            let timestamp = Utc::now().to_rfc3339();

            let conn = db.connect()?;
            
            let result = conn.execute(
                r#"INSERT OR REPLACE INTO usage_records 
                   (id, provider_id, provider_name, usage, "limit", usage_unit, is_quota_based, timestamp)
                   VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8)"#,
                (
                    id.as_str(),
                    u.provider_id.as_str(),
                    u.provider_name.as_str(),
                    u.cost_used,
                    u.cost_limit,
                    u.usage_unit.as_str(),
                    if u.is_quota_based { 1 } else { 0 },
                    timestamp.as_str(),
                ),
            ).await;

            if let Err(e) = result {
                error!("Failed to insert usage record for {}: {}", u.provider_id, e);
            }
        }
    }

    Ok(())
}
