# AI Consumption Tracker - Web Dashboard

A comprehensive web dashboard for viewing AI usage data collected by the agent service.

## Features

- **Overview Dashboard**: Real-time summary of all providers and usage statistics
- **Provider Cards**: Individual cards for each provider showing current usage and trends
- **Usage History**: Searchable and filterable table of all historical records
- **Daily Usage Charts**: Bar charts showing daily usage over time
- **Real-time Updates**: Auto-refresh every 60 seconds
- **Parallel Database Access**: Read-only connection to agent's SQLite database

## Running

```bash
cd rust
cargo run -p aic_web --port 3000
```

Options:
- `--port <number>` - Port to listen on (default: 3000)
- `--db-path <path>` - Path to agent database
- `--log-level <level>` - Logging level (default: info)

## API Endpoints

- `GET /` - Main dashboard
- `GET /api/summary` - Overall statistics
- `GET /api/providers` - List of all providers with latest usage
- `GET /api/history?provider_id=<id>&limit=<n>` - Usage history with filters
- `GET /api/daily` - Daily usage summary for charts

## Architecture

```
Web Server (Axum)
├── Static Assets (HTML, CSS, JS)
├── API Endpoints (JSON)
└── Read-Only SQLite Connection
    └── agent.db (shared with agent service)
```

## Database Schema

The dashboard reads from the same `agent.db` used by the agent:

```sql
CREATE TABLE usage_records (
    id TEXT PRIMARY KEY,
    provider_id TEXT NOT NULL,
    provider_name TEXT NOT NULL,
    usage REAL NOT NULL,
    limit REAL,
    usage_unit TEXT NOT NULL,
    is_quota_based INTEGER NOT NULL,
    timestamp TEXT NOT NULL
);
```

## Integration

The web dashboard runs in parallel with the agent service:

1. **Agent Service** (`aic_agent`) - Collects data periodically, writes to SQLite
2. **Web Dashboard** (`aic_web`) - Reads from SQLite, provides visualization

Both services can run simultaneously without conflict because:
- Web dashboard uses read-only database connection
- SQLite handles concurrent readers efficiently
- No write operations are performed by the dashboard
