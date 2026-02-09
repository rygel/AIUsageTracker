use anyhow::Result;
use axum::{
    extract::{Query, State},
    http::StatusCode,
    response::{Html, Json},
    routing::{get},
    Router,
};
use clap::Parser;
use serde::{Deserialize, Serialize};
use libsql::Builder;
use std::sync::Arc;
use tokio::sync::Mutex;
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
    db: Arc<Mutex<libsql::Database>>,
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

    let db_path = args.db_url.unwrap_or_else(|| {
        "./agent.db".to_string()
    });

    info!("Using database: {}", db_path);

    let db = Builder::new_local(&db_path).build().await?;

    info!("Connected to database successfully");

    let state = AppState {
        db: Arc::new(Mutex::new(db)),
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

    let listener = tokio::net::TcpListener::bind(format!("127.0.0.1:{}", args.port)).await?;
    info!("Web dashboard listening on http://127.0.0.1:{}", args.port);

    axum::serve(listener, app).await?;

    Ok(())
}

async fn root() -> Html<&'static str> {
    Html(include_str!("../templates/dashboard.html"))
}

async fn get_summary(
    State(state): State<AppState>,
) -> Result<Json<DashboardSummary>, StatusCode> {
    let db = state.db.lock().await;
    let conn = db.connect().map_err(|e| {
        error!("Failed to connect to database: {}", e);
        StatusCode::INTERNAL_SERVER_ERROR
    })?;

    let mut rows = conn.query("SELECT COUNT(*) FROM usage_records", ()).await.map_err(|e| {
        error!("Failed to fetch total records: {}", e);
        StatusCode::INTERNAL_SERVER_ERROR
    })?;
    
    let total_records: i64 = match rows.next().await {
        Ok(Some(row)) => row.get(0).unwrap_or(0),
        _ => 0,
    };

    let mut rows = conn.query("SELECT COUNT(DISTINCT provider_id) FROM usage_records", ()).await.map_err(|e| {
        error!("Failed to fetch total providers: {}", e);
        StatusCode::INTERNAL_SERVER_ERROR
    })?;
    
    let total_providers: i64 = match rows.next().await {
        Ok(Some(row)) => row.get(0).unwrap_or(0),
        _ => 0,
    };

    let mut rows = conn.query("SELECT COALESCE(SUM(usage), 0) FROM usage_records", ()).await.map_err(|e| {
        error!("Failed to fetch total usage: {}", e);
        StatusCode::INTERNAL_SERVER_ERROR
    })?;
    
    let total_usage: f64 = match rows.next().await {
        Ok(Some(row)) => row.get(0).unwrap_or(0.0),
        _ => 0.0,
    };

    let mut rows = conn.query("SELECT MAX(timestamp) FROM usage_records", ()).await.map_err(|e| {
        error!("Failed to fetch last updated: {}", e);
        StatusCode::INTERNAL_SERVER_ERROR
    })?;
    
    let last_updated: Option<String> = match rows.next().await {
        Ok(Some(row)) => row.get(0).ok(),
        _ => None,
    };

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
    let db = state.db.lock().await;
    let conn = db.connect().map_err(|e| {
        error!("Failed to connect to database: {}", e);
        StatusCode::INTERNAL_SERVER_ERROR
    })?;

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

    let mut rows = conn.query(query, ()).await.map_err(|e| {
        error!("Failed to fetch providers: {}", e);
        StatusCode::INTERNAL_SERVER_ERROR
    })?;

    let mut providers = Vec::new();
    while let Some(row) = rows.next().await.map_err(|e| {
        error!("Failed to iterate providers: {}", e);
        StatusCode::INTERNAL_SERVER_ERROR
    })? {
        let provider_id: String = row.get(0).map_err(|_| StatusCode::INTERNAL_SERVER_ERROR)?;
        let provider_name: String = row.get(1).map_err(|_| StatusCode::INTERNAL_SERVER_ERROR)?;
        let current_usage: f64 = row.get(2).map_err(|_| StatusCode::INTERNAL_SERVER_ERROR)?;
        let last_updated: String = row.get(3).map_err(|_| StatusCode::INTERNAL_SERVER_ERROR)?;
        
        providers.push(ProviderInfo {
            provider_id,
            provider_name,
            current_usage,
            last_updated,
        });
    }

    Ok(Json(providers))
}

async fn get_history(
    State(state): State<AppState>,
    Query(params): Query<HistoryQuery>,
) -> Result<Json<Vec<UsageRecord>>, StatusCode> {
    let db = state.db.lock().await;
    let conn = db.connect().map_err(|e| {
        error!("Failed to connect to database: {}", e);
        StatusCode::INTERNAL_SERVER_ERROR
    })?;

    let mut rows = if let (Some(provider_id), Some(limit)) = (&params.provider_id, params.limit) {
        conn.query(
            "SELECT id, provider_id, provider_name, usage, limit, usage_unit, is_quota_based, timestamp FROM usage_records WHERE provider_id = ?1 ORDER BY timestamp DESC LIMIT ?2",
            [provider_id.as_str(), limit.to_string().as_str()]
        ).await.map_err(|e| {
            error!("Failed to fetch history: {}", e);
            StatusCode::INTERNAL_SERVER_ERROR
        })?
    } else if let Some(provider_id) = &params.provider_id {
        conn.query(
            "SELECT id, provider_id, provider_name, usage, limit, usage_unit, is_quota_based, timestamp FROM usage_records WHERE provider_id = ?1 ORDER BY timestamp DESC",
            [provider_id.as_str()]
        ).await.map_err(|e| {
            error!("Failed to fetch history: {}", e);
            StatusCode::INTERNAL_SERVER_ERROR
        })?
    } else if let Some(limit) = params.limit {
        conn.query(
            "SELECT id, provider_id, provider_name, usage, limit, usage_unit, is_quota_based, timestamp FROM usage_records ORDER BY timestamp DESC LIMIT ?1",
            [limit.to_string().as_str()]
        ).await.map_err(|e| {
            error!("Failed to fetch history: {}", e);
            StatusCode::INTERNAL_SERVER_ERROR
        })?
    } else {
        conn.query(
            "SELECT id, provider_id, provider_name, usage, limit, usage_unit, is_quota_based, timestamp FROM usage_records ORDER BY timestamp DESC",
            ()
        ).await.map_err(|e| {
            error!("Failed to fetch history: {}", e);
            StatusCode::INTERNAL_SERVER_ERROR
        })?
    };

    let mut records = Vec::new();
    while let Some(row) = rows.next().await.map_err(|e| {
        error!("Failed to iterate history: {}", e);
        StatusCode::INTERNAL_SERVER_ERROR
    })? {
        let id: String = row.get(0).map_err(|_| StatusCode::INTERNAL_SERVER_ERROR)?;
        let provider_id: String = row.get(1).map_err(|_| StatusCode::INTERNAL_SERVER_ERROR)?;
        let provider_name: String = row.get(2).map_err(|_| StatusCode::INTERNAL_SERVER_ERROR)?;
        let usage: f64 = row.get(3).map_err(|_| StatusCode::INTERNAL_SERVER_ERROR)?;
        let limit: Option<f64> = row.get(4).ok();
        let usage_unit: String = row.get(5).map_err(|_| StatusCode::INTERNAL_SERVER_ERROR)?;
        let is_quota_based: bool = row.get::<i64>(6).map_err(|_| StatusCode::INTERNAL_SERVER_ERROR)? == 1;
        let timestamp: String = row.get(7).map_err(|_| StatusCode::INTERNAL_SERVER_ERROR)?;
        
        records.push(UsageRecord {
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

async fn get_daily_usage(
    State(state): State<AppState>,
    Query(params): Query<HistoryQuery>,
) -> Result<Json<Vec<DailyUsage>>, StatusCode> {
    let db = state.db.lock().await;
    let conn = db.connect().map_err(|e| {
        error!("Failed to connect to database: {}", e);
        StatusCode::INTERNAL_SERVER_ERROR
    })?;

    let mut rows = if let Some(provider_id) = &params.provider_id {
        conn.query(
            "SELECT DATE(timestamp) as date, SUM(usage) as total_usage, COUNT(*) as record_count FROM usage_records WHERE provider_id = ?1 GROUP BY DATE(timestamp) ORDER BY date DESC LIMIT 30",
            [provider_id.as_str()]
        ).await.map_err(|e| {
            error!("Failed to fetch daily usage: {}", e);
            StatusCode::INTERNAL_SERVER_ERROR
        })?
    } else {
        conn.query(
            "SELECT DATE(timestamp) as date, SUM(usage) as total_usage, COUNT(*) as record_count FROM usage_records GROUP BY DATE(timestamp) ORDER BY date DESC LIMIT 30",
            ()
        ).await.map_err(|e| {
            error!("Failed to fetch daily usage: {}", e);
            StatusCode::INTERNAL_SERVER_ERROR
        })?
    };

    let mut daily_usage = Vec::new();
    while let Some(row) = rows.next().await.map_err(|e| {
        error!("Failed to iterate daily usage: {}", e);
        StatusCode::INTERNAL_SERVER_ERROR
    })? {
        let date: String = row.get(0).map_err(|_| StatusCode::INTERNAL_SERVER_ERROR)?;
        let total_usage: f64 = row.get(1).map_err(|_| StatusCode::INTERNAL_SERVER_ERROR)?;
        let record_count: i64 = row.get(2).map_err(|_| StatusCode::INTERNAL_SERVER_ERROR)?;
        
        daily_usage.push(DailyUsage {
            date,
            total_usage,
            record_count: record_count as usize,
        });
    }

    Ok(Json(daily_usage))
}
