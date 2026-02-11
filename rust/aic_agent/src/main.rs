use anyhow::Result;
use axum::{
    extract::{Path, Query, State},
    http::StatusCode,
    response::Json,
    routing::{get, post},
    Router,
};
use chrono::{DateTime, Utc};
use clap::Parser;
use serde::{Deserialize, Serialize};
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

    let config = Arc::new(RwLock::new(AgentConfig {
        refresh_interval_minutes: args.refresh_interval_minutes,
        auto_refresh_enabled: true,
        discovered_providers: Vec::new(),
    }));

    let state = AppState {
        db: Arc::new(db),
        config,
    };

    let scheduler_handle = start_scheduler(state.clone()).await?;

    let app = Router::new()
        .route("/health", get(health_check))
        .route("/api/providers/usage", get(get_current_usage))
        .route("/api/providers/usage/refresh", post(trigger_refresh))
        .route("/api/providers/:id/usage", get(get_provider_usage))
        .route("/api/providers/discovered", get(get_discovered_providers))
        .route("/api/history", get(get_historical_usage))
        .route("/api/config", get(get_config))
        .route("/api/config", post(update_config))
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
    use aic_core::config::ProviderManager;

    let db = state.db.clone();
    let config = state.config.clone();

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
                    if let Err(e) = refresh_and_store(&db).await {
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

async fn get_current_usage(
    State(state): State<AppState>,
) -> Result<Json<Vec<ProviderUsage>>, StatusCode> {
    info!("API: GET /api/providers/usage - Fetching current usage");
    
    let client = reqwest::Client::new();
    let provider_manager = aic_core::config::ProviderManager::new(client);

    let usages = provider_manager.get_all_usage(false).await;
    
    info!("API: Returning {} usage records", usages.len());
    
    // Log details of each provider
    for usage in &usages {
        info!(
            "  Provider: {} ({}), Available: {}, Cost: {:.2} / {:.2}",
            usage.provider_name,
            usage.provider_id,
            usage.is_available,
            usage.cost_used,
            usage.cost_limit
        );
    }

    Ok(Json(usages))
}

async fn trigger_refresh(
    State(state): State<AppState>,
) -> Result<Json<Vec<ProviderUsage>>, StatusCode> {
    info!("API: POST /api/providers/usage/refresh - Refreshing usage data");
    
    let client = reqwest::Client::new();
    let provider_manager = aic_core::config::ProviderManager::new(client);

    info!("API: Fetching fresh data from providers...");
    let usages = provider_manager.get_all_usage(true).await;
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
    
    let client = reqwest::Client::new();
    let provider_manager = aic_core::config::ProviderManager::new(client);

    let usages = provider_manager.get_all_usage(false).await;
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

async fn refresh_and_store(db: &libsql::Database) -> Result<()> {
    let client = reqwest::Client::new();
    let provider_manager = aic_core::config::ProviderManager::new(client);

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
