# AI Consumption Tracker - Rust Port

A Rust port of the AI Consumption Tracker application with modular architecture for agent, CLI, web, and Tauri desktop app.

## Architecture

```
┌─────────────────────────────────────────────┐
│          Agent Service (Write)                  │
│  ┌─────────────────────────────────────┐      │
│  │ SQLite Database (agent.db)       │      │
│  └─────────────────────────────────────┘      │
│  │  ↕  ↕  ↕  ↕  ↕                │
│  │  Multiple Read Connections                   │
│  └─────────────────────────────────────┘      │
└─────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────┐
│           Web Dashboard (Read-Only)           │
│  ┌─────────────────────────────────────┐      │
│  │ Axum HTTP Server (port 3000)    │      │
│  ├─ API Endpoints (JSON)              │      │
│  └─ Static Assets (HTML/CSS/JS)        │
│  └─────────────────────────────────────┘      │
└─────────────────────────────────────────────────────┘
```

## Components

### 1. Core Library (`aic_core`)
- **Purpose**: Shared models, providers, and business logic
- **Location**: `rust/aic_core/`
- **Key Features**:
  - Provider interface with 15+ AI service implementations
  - Configuration loading from multiple sources (auth.json, env vars, Kilo Code)
  - Privacy/content masking utilities

### 2. Agent Service (`aic_agent`)
- **Purpose**: Background service collecting provider data periodically
- **Location**: `rust/aic_agent/`
- **Default Port**: 8080
- **Key Features**:
  - Auto-refresh with configurable interval (default: 5 minutes)
  - SQLite database storage for historical records
  - HTTP API for data queries
  - Runs as daemon/service

### 3. CLI (`aic_cli`)
- **Purpose**: Command-line interface for querying and managing
- **Location**: `rust/aic_cli/`
- **Binary Name**: `aic-cli`
- **Key Features**:
  - Connects to agent service via HTTP API
  - Commands: `status`, `list`, `auth`, `logout`, `refresh`, `history`, `health`
  - JSON and table output formats
  - Authentication with GitHub device flow

### 4. Web Dashboard (`aic_web`)
- **Purpose**: Web-based visualization dashboard
- **Location**: `rust/aic_web/`
- **Default Port**: 3000
- **Key Features**:
  - Read-only connection to shared SQLite database
  - Overview summary with key metrics
  - Provider cards with usage trends
  - Usage history table with filters
  - Daily usage charts (bar charts)
  - Auto-refresh every 60 seconds
  - Responsive dark theme design

### 5. Tauri Desktop App (`aic_app`)
- **Purpose**: Native desktop application with Tauri
- **Location**: `rust/aic_app/`
- **Key Features**:
  - Tauri IPC for backend communication
  - Modern dark theme UI
  - Real-time provider cards with progress bars
  - GitHub device flow authentication
  - Refresh and filter functionality

## Building

```bash
cd rust

# Build all components
cargo build

# Build specific component
cargo build -p aic_agent
cargo build -p aic_cli
cargo build -p aic_web
cargo build -p aic_app

# Build for production
cargo build --release
```

## Running Components

### Agent Service
```bash
cd rust

# Start agent (default port 8080, refresh every 5 minutes)
cargo run -p aic_agent

# Custom port and refresh interval
cargo run -p aic_agent --port 9000 --refresh-interval-minutes 10
```

### CLI
```bash
cd rust

# Get current usage (connects to agent)
cargo run -p aic_cli -- status

# Trigger refresh
cargo run -p aic_cli -- refresh

# Show historical data
cargo run -p aic_cli -- history --limit 20

# Check agent health
cargo run -p aic_cli -- health

# Custom agent URL
cargo run -p aic_cli --agent-url http://localhost:9000 -- status
```

### Web Dashboard
```bash
cd rust

# Start web dashboard (default port 3000)
cargo run -p aic_web

# Custom port
cargo run -p aic_web --port 4000

# Custom database path
cargo run -p aic_web --db-path /path/to/agent.db

# Verbose logging
cargo run -p aic_web --log-level debug
```

### Tauri Desktop App
```bash
cd rust

# Start Tauri app
cargo run -p aic_app

# Build and run Tauri
cargo tauri build
cargo tauri dev
```

## Testing

```bash
cd rust

# Run all tests
cargo test

# Run tests sequentially (for consistency)
cargo test -- --test-threads=1

# Run specific test
cargo test -p aic_core
cargo test -p aic_cli
cargo test -p aic_agent
cargo test -p aic_app

# Run specific test file
cargo test -p aic_core --test provider_tests
```

## Database Schema

The SQLite database (`agent.db`) is shared between agent and web dashboard:

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

-- Indexes for performance
CREATE INDEX idx_provider_id ON usage_records(provider_id);
CREATE INDEX idx_timestamp ON usage_records(timestamp);
CREATE INDEX idx_provider_timestamp ON usage_records(provider_id, timestamp);
```

## API Endpoints

### Agent Service (port 8080)
- `GET /health` - Health check
- `GET /api/providers/usage` - Get current usage
- `POST /api/providers/usage/refresh` - Trigger manual refresh
- `GET /api/providers/:id/usage` - Get specific provider
- `GET /api/history` - Get historical records
- `GET /api/config` - Get configuration
- `POST /api/config` - Update configuration

### Web Dashboard (port 3000)
- `GET /` - Main dashboard UI
- `GET /api/summary` - Overall statistics
- `GET /api/providers` - Provider list with latest usage
- `GET /api/history` - Historical records
- `GET /api/daily` - Daily usage summary

## Configuration Files

All components read from the same configuration file:
- `~/.ai-consumption-tracker/auth.json` - Primary config
- Environment variables for API keys (OPENAI_API_KEY, ANTHROPIC_API_KEY, etc.)
- `~/.kilocode/secrets.json` - Kilo Code integration

## Development Workflow

1. Start agent service:
   ```bash
   cd rust && cargo run -p aic_agent
   ```

2. Start web dashboard (in separate terminal):
   ```bash
   cd rust && cargo run -p aic_web
   ```

3. Open browser to http://localhost:3000

4. Agent will collect data periodically and store in SQLite

5. Web dashboard reads from SQLite in real-time (auto-refresh every 60s)

## License

MIT
