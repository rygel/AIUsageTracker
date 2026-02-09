use anyhow::Result;
use axum::{
    extract::{Query, State},
    http::StatusCode,
    response::{Html, Json},
    routing::{get},
    Router,
};
use chrono::{DateTime, Utc};
use clap::Parser;
use serde::{Deserialize, Serialize};
use libsql::Connection;
use std::path::PathBuf;
use tower_http::services::ServeDir;
use tracing::{info, error};
use tracing_subscriber::{layer::SubscriberExt, util::SubscriberInitExt};

#[derive(Parser, Debug)]
#[command(author, version, about, long_about = None)]
struct Args {
    #[arg(short, long, default_value_t = 3000)]
    port: u16,

    #[arg(long)]
    db_url: Option<String>,

    #[arg(long, default_value = "info")]
    log_level: String,
}

#[derive(Clone)]
struct AppState {
    db: Arc<Connection>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
struct DashboardSummary {
    total_providers: usize,
    total_records: usize,
    total_usage: f64,
    last_updated: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
struct ProviderInfo {
    provider_id: String,
    provider_name: String,
    current_usage: f64,
    last_updated: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
struct UsageRecord {
    id: String,
    provider_id: String,
    provider_name: String,
    usage: f64,
    limit: Option<f64>,
    usage_unit: String,
    is_quota_based: bool,
    timestamp: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
struct DailyUsage {
    date: String,
    total_usage: f64,
    record_count: usize,
}

#[derive(Debug, Deserialize)]
struct HistoryQuery {
    provider_id: Option<String>,
    limit: Option<usize>,
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

    info!("Starting AI Consumption Tracker Web Dashboard v{}", env!("CARGO_PKG_VERSION"));

    let db_url = args.db_url.unwrap_or_else(|| {
        "file:./agent.db".to_string()
    });

    info!("Using database: {}", db_url);

    let db = Connection::open(&db_url)?;

    info!("Connected to database successfully");

    let state = AppState {
        db: Arc::new(db),
    };

    let static_files = ServeDir::new("static").fallback(ServeDir::new("templates"));

    let app = Router::new()
        .route("/", get(root))
        .route("/api/summary", get(get_summary))
        .route("/api/providers", get(get_providers))
        .route("/api/history", get(get_history))
        .route("/api/daily", get(get_daily_usage))
        .nest_service("/static", static_files)
        .with_state(state);

    let listener = tokio::net::TcpListener::bind(format!("0.0.0.0:{}", args.port)).await?;
    info!("Web dashboard listening on http://0.0.0.0:{}", args.port);

    axum::serve(listener, app).await?;

    Ok(())
}

async fn root() -> Html<&'static str> {
    Html(include_str!("../templates/dashboard.html"))
}

async fn get_summary(
    State(state): State<AppState>,
) -> Result<Json<DashboardSummary>, StatusCode> {
    let total_records: i64 = state.db.execute("SELECT COUNT(*) FROM usage_records").fetch_one::<i64, _>([]).await.unwrap_or(0);

    let total_providers: i64 = state.db.execute("SELECT COUNT(DISTINCT provider_id) FROM usage_records").fetch_one::<i64, _>([]).await.unwrap_or(0);

    let total_usage: f64 = state.db.execute("SELECT COALESCE(SUM(usage), 0) FROM usage_records").fetch_one::<f64, _>([]).await.unwrap_or(0.0);

    let last_updated: Option<String> = state.db.query("SELECT MAX(timestamp) FROM usage_records").fetch_one::<String, _>([]).await.ok();

    Ok(Json(DashboardSummary {
        total_providers: total_providers as usize,
        total_records: total_records as usize,
        total_usage,
        last_updated,
    }))
}

async fn get_providers(
    State(state): State<AppState>,
) -> Result<Json<Vec<ProviderInfo>>, StatusCode> {
    let query = r#"
        SELECT provider_id, provider_name, usage, timestamp
        FROM usage_records ur1
        WHERE timestamp = (
            SELECT MAX(ur2.timestamp)
            FROM usage_records ur2
            WHERE ur2.provider_id = ur1.provider_id
        )
        ORDER BY provider_name
        "#;

    let providers = state.db.query::<(String, String, f64, String)>(query).fetch_all().await.map_err(|e| {
        error!("Failed to fetch providers: {}", e);
        StatusCode::INTERNAL_SERVER_ERROR
    })?;

    let result: Vec<ProviderInfo> = providers.into_iter()
        .map(|p| ProviderInfo {
            provider_id: p.0,
            provider_name: p.1,
            current_usage: p.2,
            last_updated: p.3,
        })
        .collect();

    Ok(Json(result))
}

async fn get_history(
    State(state): State<AppState>,
    Query(params): Query<HistoryQuery>,
) -> Result<Json<Vec<UsageRecord>>, StatusCode> {
    let mut query = "SELECT id, provider_id, provider_name, usage, limit, usage_unit, is_quota_based, timestamp FROM usage_records".to_string();

    if let Some(provider_id) = &params.provider_id {
        query = format!("{} WHERE provider_id = '{}'", query, provider_id);
    }

    query = format!("{} ORDER BY timestamp DESC", query);

    if let Some(limit) = params.limit {
        query = format!("{} LIMIT {}", query, limit);
    }

    let records = state.db.query::<(String, String, f64, Option<f64>, String, bool, String)>(&query).fetch_all().await.map_err(|e| {
        error!("Failed to fetch history: {}", e);
        StatusCode::INTERNAL_SERVER_ERROR
    })?;

    Ok(Json(records))
}

async fn get_daily_usage(
    State(state): State<AppState>,
    Query(params): Query<HistoryQuery>,
) -> Result<Json<Vec<DailyUsage>>, StatusCode> {
    let mut query = "SELECT DATE(timestamp) as date, SUM(usage) as total_usage, COUNT(*) as record_count FROM usage_records".to_string();

    if let Some(provider_id) = &params.provider_id {
        query = format!("{} WHERE provider_id = '{}'", query, provider_id);
    }

    query = format!("{} GROUP BY DATE(timestamp) ORDER BY date DESC LIMIT 30", query);

    let records = state.db.query::<(String, f64, i64)>(&query).fetch_all().await.map_err(|e| {
        error!("Failed to fetch daily usage: {}", e);
        StatusCode::INTERNAL_SERVER_ERROR
    })?;

    Ok(Json(records))
}
