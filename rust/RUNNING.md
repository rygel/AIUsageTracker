# Running Rust Applications

This guide provides comprehensive instructions for building and running all Rust applications in the AI Consumption Tracker project.

## Table of Contents

- [Prerequisites](#prerequisites)
- [Quick Start](#quick-start)
- [Applications](#applications)
  - [CLI (opencode-tracker)](#cli-opencode-tracker)
  - [Agent Service (aic_agent)](#agent-service-aic_agent)
  - [Web Dashboard (aic_web)](#web-dashboard-aic_web)
  - [Tauri Desktop App (aic_app)](#tauri-desktop-app-aic_app)
- [Configuration](#configuration)
- [Database Setup](#database-setup)
- [Development Workflow](#development-workflow)
- [Testing](#testing)
- [Troubleshooting](#troubleshooting)

## Prerequisites

### Required Tools

- **Rust**: 1.70.0 or later
  ```bash
  rustc --version
  ```

- **Cargo**: Comes with Rust
  ```bash
  cargo --version
  ```

### Platform-Specific Dependencies

#### Windows
- **Visual Studio Build Tools**: For compiling native dependencies
  - Download from: https://visualstudio.microsoft.com/downloads/
  - Install "Desktop development with C++" workload

#### Linux
- **pkg-config**: For Tauri webview2 dependencies
  ```bash
  sudo apt-get install libwebkit2gtk-4.1-dev
  sudo apt-get install libssl-dev
  sudo apt-get install libayatana-appindicator3-dev
  sudo apt-get install librsvg2-dev
  ```

#### macOS
- **Xcode Command Line Tools**:
  ```bash
  xcode-select --install
  ```

### Install Rust (if not already installed)

```bash
curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh
```

## Quick Start

Build all applications in release mode:

```bash
cd rust
cargo build --release --workspace
```

Or build individual applications:

```bash
cargo build --release -p aic_cli    # CLI
cargo build --release -p aic_agent   # Agent service
cargo build --release -p aic_web     # Web dashboard
cargo build --release -p aic_app     # Tauri app (requires Tauri CLI)
```

## Applications

### CLI (opencode-tracker)

Command-line interface for checking AI provider usage and managing configurations.

#### Build

```bash
cargo build --release -p aic_cli
```

Binary location: `target/release/opencode-tracker` (Linux/macOS) or `target/release/opencode-tracker.exe` (Windows)

#### Run Commands

```bash
# Show current usage for all providers
cargo run -p aic_cli -- status

# Show usage in JSON format
cargo run -p aic_cli -- status --json

# List configured providers
cargo run -p aic_cli -- list
```

#### Environment Variables

```bash
# Set log level
export RUST_LOG=debug
export RUST_LOG=info
export RUST_LOG=warn

# Path to config file (default: ~/.ai-consumption-tracker/auth.json)
export AICT_CONFIG_PATH=/path/to/config.json
```

### Agent Service (aic_agent)

Background service with HTTP API and libsql database for historical usage tracking.

#### Build

```bash
cargo build --release -p aic_agent
```

Binary location: `target/release/aic_agent` (Linux/macOS) or `target/release/aic_agent.exe` (Windows)

#### Run

```bash
# Run with default settings (port 8080, 5 minute refresh interval)
cargo run -p aic_agent

# Custom port
cargo run -p aic_agent -- --port 3000

# Custom refresh interval (in minutes)
cargo run -p aic_agent -- --refresh-interval-minutes 10

# Debug logging
cargo run -p aic_agent -- --log-level debug

# Using release build
./target/release/aic_agent -- --port 3000
```

#### Command-Line Arguments

| Argument | Short | Default | Description |
|----------|--------|---------|-------------|
| `--port` | `-p` | 8080 | HTTP server port |
| `--db-url` | | None | libsql database URL (local file or Turso cloud) |
| `--refresh-interval-minutes` | | 5 | Auto-refresh interval in minutes |
| `--log-level` | | info | Logging level (error, warn, info, debug, trace) |

#### HTTP API Endpoints

| Endpoint | Method | Description |
|----------|---------|-------------|
| `/health` | GET | Health check |
| `/api/providers/usage` | GET | Get current usage from providers |
| `/api/providers/usage/refresh` | POST | Trigger manual refresh |
| `/api/providers/:id/usage` | GET | Get usage for specific provider |
| `/api/history` | GET | Get historical usage records |
| `/api/config` | GET | Get agent configuration |
| `/api/config` | POST | Update agent configuration |

#### Example Queries

```bash
# Health check
curl http://localhost:8080/health

# Get current usage
curl http://localhost:8080/api/providers/usage

# Get history with filters
curl "http://localhost:8080/api/history?provider_id=openai&limit=10"

# Get history by date range
curl "http://localhost:8080/api/history?start_date=2026-02-01T00:00:00Z&end_date=2026-02-09T23:59:59Z"

# Trigger refresh
curl -X POST http://localhost:8080/api/providers/usage/refresh

# Get provider-specific usage
curl http://localhost:8080/api/providers/openai/usage

# Update config
curl -X POST http://localhost:8080/api/config \
  -H "Content-Type: application/json" \
  -d '{"refresh_interval_minutes": 10, "auto_refresh_enabled": true}'
```

### Web Dashboard (aic_web)

Web interface for visualizing usage history and provider statistics using libsql database.

#### Build

```bash
cargo build --release -p aic_web
```

Binary location: `target/release/aic_web` (Linux/macOS) or `target/release/aic_web.exe` (Windows)

#### Run

```bash
# Run with default port (3000)
cargo run -p aic_web

# Custom port
cargo run -p aic_web -- --port 8080

# Using release build
./target/release/aic_web -- --port 8080

# Connect to shared libsql database
cargo run -p aic_web -- --db-url ./agent.db
```

#### Command-Line Arguments

| Argument | Short | Default | Description |
|----------|--------|---------|-------------|
| `--port` | `-p` | 3000 | HTTP server port |
| `--db-url` | | ./agent.db | libsql database file path or Turso URL |
| `--log-level` | | info | Logging level |

#### Accessing the Dashboard

Open your browser to:
- Default: http://localhost:3000
- Custom port: http://localhost:8080

### Tauri Desktop App (aic_app)

Cross-platform desktop application with system tray, auto-refresh, and modern dark theme.

#### Prerequisites for Tauri

Install Tauri CLI globally:

```bash
cargo install tauri-cli
```

#### Build

```bash
# Development build (with devtools)
cargo tauri dev

# Release build
cargo tauri build
```

Release binaries location:
- Windows: `src-tauri/target/release/bundle/msi/`
- macOS: `src-tauri/target/release/bundle/dmg/`
- Linux: `src-tauri/target/release/bundle/deb/` or `src-tauri/target/release/bundle/appimage/`

#### Run Development Server

```bash
cargo tauri dev
```

This starts:
- Tauri development server
- Frontend hot-reload
- Debug console output

#### Features

- **System Tray**: Minimize to system tray, show notifications
- **Auto-Refresh**: Configurable interval (default: 5 minutes)
- **Privacy Mode**: Blur/hide sensitive data
- **Dark Theme**: Modern, consistent with other applications
- **Real-time Updates**: Live provider status updates
- **Offline Support**: Cache provider configurations locally

## Configuration

### Configuration File Location

Applications read configuration from: `~/.ai-consumption-tracker/auth.json`

Example structure:

```json
{
  "providers": {
    "openai": {
      "api_key": "sk-...",
      "provider_id": "openai",
      "provider_name": "OpenAI"
    },
    "anthropic": {
      "api_key": "sk-ant-...",
      "provider_id": "anthropic",
      "provider_name": "Anthropic"
    }
  },
  "app_preferences": {
    "auto_refresh_enabled": true,
    "refresh_interval_minutes": 5,
    "privacy_mode_enabled": false
  }
}
```

### Environment Variables

```bash
# OpenAI API Key
export OPENAI_API_KEY="sk-..."

# Anthropic API Key
export ANTHROPIC_API_KEY="sk-ant-..."

# DeepSeek API Key
export DEEPSEEK_API_KEY="sk-..."

# Google API Key (for Gemini)
export GOOGLE_API_KEY="AIza..."

# GitHub Token (for Copilot)
export GITHUB_TOKEN="ghp_..."

# Configuration path override
export AICT_CONFIG_PATH="/custom/path/auth.json"

# Database URL (for agent and web)
export TURSO_DATABASE_URL="libsql://your-db-url.turso.io"
export TURSO_AUTH_TOKEN="your-auth-token"
```

### Priority Order for API Keys

1. Configuration file (`~/.ai-consumption-tracker/auth.json`)
2. Environment variables
3. Kilo Code secrets (Tauri app only)

## Database Setup

### Local Database (Default)

Both `aic_agent` and `aic_web` use a local libsql database by default:

- **File**: `agent.db` in current working directory
- **Format**: SQLite-compatible (libsql)
- **Schema**: Auto-created on first run

### Using Turso Cloud Database

To use Turso cloud instead of local database:

#### Option 1: Environment Variables

```bash
export TURSO_DATABASE_URL="libsql://your-db-url.turso.io"
export TURSO_AUTH_TOKEN="your-auth-token"

cargo run -p aic_agent
cargo run -p aic_web
```

#### Option 2: Command-Line Argument

```bash
cargo run -p aic_agent -- --db-url "libsql://your-db-url.turso.io?authToken=your-auth-token"
cargo run -p aic_web -- --db-url "libsql://your-db-url.turso.io?authToken=your-auth-token"
```

#### Create Turso Database

```bash
# Install Turso CLI
curl -sSfL https://get.tur.so/install.sh | bash

# Create database
turso db create ai-consumption-tracker

# Get database URL
turso db show ai-consumption-tracker --url

# Create auth token
turso db tokens create ai-consumption-tracker
```

#### Database Schema

The database is automatically initialized with the following schema:

```sql
CREATE TABLE usage_records (
    id TEXT PRIMARY KEY,
    provider_id TEXT NOT NULL,
    provider_name TEXT NOT NULL,
    usage REAL NOT NULL,
    "limit" REAL,
    usage_unit TEXT NOT NULL,
    is_quota_based INTEGER NOT NULL,
    timestamp TEXT NOT NULL
);

CREATE INDEX idx_provider_id ON usage_records(provider_id);
CREATE INDEX idx_timestamp ON usage_records(timestamp);
CREATE INDEX idx_provider_timestamp ON usage_records(provider_id, timestamp);
```

### Sharing Database Between Services

You can share the same database file between `aic_agent` and `aic_web`:

```bash
# Agent writes to agent.db
cargo run -p aic_agent -- --db-url ./shared.db

# Web reads from agent.db (same file)
cargo run -p aic_web -- --db-url ./shared.db
```

## Development Workflow

### Development Build

```bash
# Build with debug symbols and no optimization
cargo build

# Run specific package in development mode
cargo run -p aic_cli -- status
cargo run -p aic_agent -- --log-level debug
cargo run -p aic_web -- --port 8080
```

### Watch Mode (CLI development)

```bash
# Install cargo-watch for auto-rebuild on changes
cargo install cargo-watch

# Auto-rebuild on file changes
cargo watch -x 'run -p aic_cli -- status'
```

### Cross-Compilation

Use `cross` for cross-compiling to different platforms:

```bash
# Install cross
cargo install cross

# Cross-compile for Windows x64
cross build --release --target x86_64-pc-windows-gnu -p aic_cli

# Cross-compile for Linux ARM64
cross build --release --target aarch64-unknown-linux-gnu -p aic_agent

# Cross-compile for macOS ARM64
cross build --release --target aarch64-apple-darwin -p aic_web
```

## Testing

### Run All Tests

```bash
# Test all packages in workspace
cargo test

# Run tests with output
cargo test -- --nocapture

# Run tests with specific log level
RUST_LOG=debug cargo test
```

### Run Specific Test Suites

```bash
# Test only core library
cargo test -p aic_core

# Test CLI
cargo test -p aic_cli

# Test agent with database tests
cargo test -p aic_agent database::

# Run specific test
cargo test -p aic_core test_provider_manager_initializes_correctly
```

### Test Coverage

```bash
# Install tarpaulin for coverage reports
cargo install cargo-tarpaulin

# Generate coverage report
cargo tarpaulin --out Html --target-dir coverage
```

### Database Tests

The `aic_agent` package includes comprehensive database tests (11 tests):

```bash
cargo test -p aic_agent database::tests
```

Tests cover:
- Database creation and migration
- CRUD operations (insert, query, update)
- Provider filtering
- Date range queries
- Pagination
- Upsert behavior
- Record cleanup by age
- NULL value handling
- Chronological ordering

## Troubleshooting

### Common Issues

#### "linking with `link.exe` failed" (Windows)

**Solution**: Install Visual Studio Build Tools with C++ workload:
1. Download Visual Studio Installer
2. Install "Desktop development with C++"

#### "can't find crate for `xxx`"

**Solution**: Update dependencies:
```bash
cargo update
```

#### "command not found: tauri"

**Solution**: Install Tauri CLI:
```bash
cargo install tauri-cli
```

#### Database file locked error

**Solution**: Close all applications using the database or delete the lock file:
```bash
rm agent.db-shm  # SQLite shared memory file
rm agent.db-wal  # SQLite write-ahead log
```

#### Permission denied on configuration file

**Solution**: Check file permissions:
```bash
ls -la ~/.ai-consumption-tracker/auth.json
chmod 600 ~/.ai-consumption-tracker/auth.json
```

### Debug Mode

Run applications with debug logging to diagnose issues:

```bash
# Set log level to debug
RUST_LOG=debug cargo run -p aic_agent -- --log-level debug

# Set log level to trace (most verbose)
RUST_LOG=trace cargo run -p aic_web -- --log-level trace

# Enable tracing for specific module
RUST_LOG=aic_core::providers=debug,libsql=info cargo run -p aic_agent
```

### Clean Build Artifacts

```bash
# Clean all build artifacts
cargo clean

# Clean specific package
cargo clean -p aic_agent

# Clean release builds only
cargo clean --release
```

### Update Dependencies

```bash
# Update all dependencies
cargo update

# Update specific dependency
cargo update libsql

# Update to latest compatible versions
cargo upgrade
```

## Performance Tips

### Release Builds

Release builds are significantly faster and smaller:

```bash
cargo build --release
```

### Profile-Guided Optimization

For maximum performance after profiling:

```bash
# Generate profile data
cargo build --profile pgo -p aic_agent

# Run typical workload
./target/pgo/aic_agent -- --port 8080

# Build with profile data
cargo build --release -p aic_agent
```

### Reduce Binary Size

The project already uses release optimizations:
- `lto = true` - Link-time optimization
- `opt-level = "s"` - Optimize for size
- `strip = true` - Remove debug symbols

Additional reduction with UPX:
```bash
upx --best --lzma target/release/aic_agent
```

## Resources

- [Rust Book](https://doc.rust-lang.org/book/)
- [Cargo Book](https://doc.rust-lang.org/cargo/)
- [Tauri Documentation](https://v2.tauri.app/)
- [libsql/Turso Documentation](https://docs.turso.tech/)
- [Tokio Runtime](https://tokio.rs/)
