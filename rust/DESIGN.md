# Design Document - Rust AI Consumption Tracker

## 1. Project Overview

### Purpose
A cross-platform application for tracking AI service usage across multiple providers (OpenAI, Anthropic, Google, etc.) with real-time monitoring and cost analysis.

### Goals
- **Performance**: Sub-second response times through caching and parallel processing
- **Cross-platform**: Native support for Windows, Linux, and macOS
- **Extensibility**: Plugin architecture for adding new AI providers
- **User Experience**: Minimal resource footprint with system tray integration
- **Reliability**: Graceful handling of network failures and provider outages

## 2. Architecture

### 2.1 High-Level Design

```
┌─────────────────────────────────────────────────────────────┐
│                      User Interfaces                        │
├─────────────┬─────────────┬─────────────────────────────────┤
│  Desktop    │    CLI      │           Web Browser           │
│  (Tauri)    │  (Crossterm)│                                 │
└──────┬──────┴──────┬──────┴────────────────┬────────────────┘
       │             │                       │
       └─────────────┴───────────────────────┘
                       │
              HTTP/WebSocket
                       │
       ┌───────────────▼───────────────┐
       │        aic_agent              │
       │    (Background Service)       │
       │         Port 8080             │
       └───────────────┬───────────────┘
                       │
       ┌───────────────┼───────────────┐
       │               │               │
┌──────▼──────┐ ┌──────▼──────┐ ┌──────▼──────┐
│  Provider   │ │  Provider   │ │  Provider   │
│  Cache      │ │   Manager   │ │    Config   │
│  (In-Mem)   │ │             │ │   (JSON)    │
└─────────────┘ └──────┬──────┘ └─────────────┘
                       │
       ┌───────────────┼───────────────┐
       │               │               │
┌──────▼──────┐ ┌──────▼──────┐ ┌──────▼──────┐
│   OpenAI    │ │  Anthropic  │ │    Google   │
│   Client    │ │    Client   │ │    Client   │
└─────────────┘ └─────────────┘ └─────────────┘
```

### 2.2 Crate Structure

| Crate | Purpose | Dependencies |
|-------|---------|--------------|
| `aic_core` | Core business logic, provider implementations, models | `serde`, `tokio`, `reqwest` |
| `aic_agent` | HTTP service, caching, provider coordination | `axum`, `tower`, `aic_core` |
| `aic_app` | Tauri-based desktop GUI | `tauri`, `tauri-build`, `aic_core` |
| `aic_cli` | Command-line interface | `clap`, `crossterm`, `aic_core` |
| `aic_web` | Web dashboard (WASM-compatible) | `leptos` or `yew`, `trunk` |

## 3. Component Details

### 3.1 aic_core

#### Models
```rust
// Provider configuration
pub struct ProviderConfig {
    pub provider_id: String,
    pub api_key: Option<String>,
    pub enabled: bool,
    pub auth_source: AuthSource, // Env, Config, None
}

// Usage data
pub struct ProviderUsage {
    pub provider_id: String,
    pub provider_name: String,
    pub is_available: bool,
    pub description: Option<String>,
    pub usage_data: Option<UsageData>,
}

pub struct UsageData {
    pub requests_count: u64,
    pub tokens_input: u64,
    pub tokens_output: u64,
    pub cost_usd: f64,
    pub period_start: DateTime<Utc>,
    pub period_end: DateTime<Utc>,
}
```

#### Provider Trait
```rust
#[async_trait]
pub trait ProviderService: Send + Sync {
    fn provider_id(&self) -> &str;
    fn provider_name(&self) -> &str;
    async fn get_usage(&self, config: &ProviderConfig) -> Result<ProviderUsage, ProviderError>;
    async fn validate_key(&self, api_key: &str) -> Result<bool, ProviderError>;
}
```

### 3.2 aic_agent

#### HTTP API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/health` | Health check with version info |
| GET | `/api/v1/providers` | List all supported providers |
| GET | `/api/v1/usage` | Get usage for all configured providers |
| GET | `/api/v1/usage/:provider` | Get usage for specific provider |
| GET | `/api/v1/config` | Get current configuration |
| POST | `/api/v1/config` | Update configuration |
| POST | `/api/v1/config/:provider` | Update specific provider config |
| POST | `/api/v1/refresh` | Force refresh of cached data |

#### Response Format
```json
{
  "version": "0.5.0",
  "providers": [
    {
      "provider_id": "openai",
      "provider_name": "OpenAI",
      "is_available": true,
      "description": null,
      "usage": {
        "requests_count": 1234,
        "tokens_input": 50000,
        "tokens_output": 25000,
        "cost_usd": 1.23,
        "period_start": "2026-02-01T00:00:00Z",
        "period_end": "2026-02-28T23:59:59Z"
      }
    }
  ]
}
```

### 3.3 aic_app (Desktop)

#### Tauri Architecture
```
aic_app/
├── src/
│   ├── main.rs          # Application entry point
│   ├── lib.rs           # Shared library code
│   ├── commands/        # Tauri command handlers
│   ├── state/           # Application state management
│   └── events/          # Event system
├── src-ui/              # Frontend assets
│   ├── index.html
│   ├── css/
│   └── js/
└── tauri.conf.json
```

#### Commands
- `get_usage()`: Fetch usage from agent
- `get_providers()`: Get provider configurations
- `update_config(provider, config)`: Update provider settings
- `set_always_on_top(enabled)`: Toggle window behavior
- `minimize_to_tray()`: Hide to system tray

#### State Management
- Uses Tauri's managed state for agent client
- Event-driven updates via `settings-window-shown` event
- Reactive UI updates on data changes

### 3.4 aic_cli

#### Commands
```bash
# Show current usage
aic_cli usage

# Show specific provider
aic_cli usage --provider openai

# Configure provider
aic_cli config set openai.api_key sk-xxx

# List providers
aic_cli providers list

# Refresh data
aic_cli refresh

# Start agent
aic_cli agent start
aic_cli agent stop
aic_cli agent status
```

## 4. Data Flow

### 4.1 Initialization Flow
```
1. Agent starts on port 8080
2. Loads configuration from ~/.config/aic/agent.json
3. Discovers environment variables (OPENAI_API_KEY, etc.)
4. Initializes provider clients with available keys
5. Fetches initial data (background thread)
6. Serves cached data immediately
```

### 4.2 Request Flow
```
1. UI/CLI makes HTTP request to agent
2. Agent checks cache validity (< 5 minutes)
3. If stale: fetch fresh data from providers (parallel)
4. Update cache with new data
5. Return cached/stale data + timestamp
6. UI displays data with "last updated" indicator
```

### 4.3 Configuration Flow
```
1. User modifies settings in UI
2. UI sends POST /api/v1/config/:provider
3. Agent validates configuration
4. Updates config file atomically
5. Triggers provider refresh
6. Returns success/failure
7. UI updates to reflect changes
```

## 5. Provider System

### 5.1 Supported Providers

| Provider | ID | Auth Method | API Endpoint |
|----------|-----|-------------|--------------|
| OpenAI | `openai` | API Key | api.openai.com |
| Anthropic | `anthropic` | API Key | api.anthropic.com |
| Google AI | `google` | API Key | generativelanguage.googleapis.com |
| Azure OpenAI | `azure-openai` | API Key + Endpoint | Custom |
| Cohere | `cohere` | API Key | api.cohere.com |
| Mistral | `mistral` | API Key | api.mistral.ai |

### 5.2 Provider Discovery
Providers are discovered through:
1. Configuration file (`~/.config/aic/agent.json`)
2. Environment variables (e.g., `OPENAI_API_KEY`)
3. Hardcoded list of supported providers

### 5.3 Error Handling
- **API Key Missing**: Mark provider as unavailable with description
- **Network Error**: Return cached data with error indicator
- **Rate Limiting**: Implement exponential backoff
- **Invalid Key**: Mark provider as failed, require re-configuration

## 6. Configuration Management

### 6.1 Configuration Locations

| Platform | Path |
|----------|------|
| Linux | `~/.config/aic/` |
| macOS | `~/Library/Application Support/aic/` |
| Windows | `%APPDATA%\aic\` |

### 6.2 Configuration Files

#### agent.json
```json
{
  "version": "0.5.0",
  "providers": {
    "openai": {
      "enabled": true,
      "api_key": null,
      "auth_source": "environment"
    },
    "anthropic": {
      "enabled": true,
      "api_key": "sk-ant-xxx",
      "auth_source": "config"
    }
  },
  "cache_ttl_seconds": 300,
  "auto_start": true
}
```

### 6.3 Security
- API keys stored in plaintext (user's responsibility to secure file)
- Environment variable keys preferred
- Config file permissions set to 0600 (owner read/write only)

## 7. Caching Strategy

### 7.1 Cache Implementation
- **Type**: In-memory HashMap with RwLock
- **TTL**: 5 minutes (configurable)
- **Scope**: Per-provider usage data
- **Invalidation**: Manual refresh or TTL expiry

### 7.2 Cache Behavior
```rust
pub struct ProviderCache {
    data: RwLock<HashMap<String, CachedUsage>>,
    ttl: Duration,
}

pub struct CachedUsage {
    usage: ProviderUsage,
    fetched_at: Instant,
}

impl ProviderCache {
    pub fn get(&self, provider_id: &str) -> Option<ProviderUsage> {
        // Return data if not expired
    }
    
    pub async fn get_or_fetch<F>(&self, provider_id: &str, fetch: F) -> ProviderUsage
    where F: Future<Output = ProviderUsage> {
        // Return cached if valid, otherwise fetch and cache
    }
}
```

## 8. Error Handling Strategy

### 8.1 Error Types
```rust
pub enum AicError {
    ConfigError(String),
    ProviderError(ProviderError),
    NetworkError(reqwest::Error),
    CacheError(String),
    IoError(std::io::Error),
}

pub enum ProviderError {
    InvalidApiKey,
    RateLimited(Duration),
    ServiceUnavailable,
    Timeout,
    ParseError(String),
}
```

### 8.2 Error Propagation
- Use `thiserror` for derive macros
- Convert low-level errors to domain errors
- Return structured error responses in HTTP API
- Log errors with context using `tracing`

## 9. Build System

### 9.1 Cargo Configuration

#### .cargo/config.toml
```toml
[build]
jobs = 12
rustflags = ["-C", "target-cpu=native"]

[profile.release]
opt-level = 3
lto = true
strip = true
```

### 9.2 Build Scripts

#### debug-build.ps1
- Validates HTML/JS syntax
- Compiles Tauri app in development mode
- Starts agent in background
- Launches UI with hot reload

#### validate.ps1
- Checks for mismatched braces/parentheses
- Validates HTML tag nesting
- Ensures no syntax errors before build

### 9.3 Cross-Platform Builds
```bash
# Windows
rustup target add x86_64-pc-windows-msvc
cargo build --release --target x86_64-pc-windows-msvc

# Linux
rustup target add x86_64-unknown-linux-gnu
cargo build --release --target x86_64-unknown-linux-gnu

# macOS
rustup target add x86_64-apple-darwin
cargo build --release --target x86_64-apple-darwin
```

## 10. Testing Strategy

### 10.1 Unit Tests
- Provider client mocking with `mockall`
- Configuration parsing tests
- Cache behavior validation

### 10.2 Integration Tests
- HTTP API endpoint testing with `axum-test`
- End-to-end CLI testing
- Provider integration (requires API keys)

### 10.3 Test Structure
```
aic_core/
├── src/
└── tests/
    ├── unit/
    │   ├── provider_tests.rs
    │   └── cache_tests.rs
    └── integration/
        ├── api_tests.rs
        └── provider_integration_tests.rs
```

## 11. Future Enhancements

### 11.1 Planned Features
- [ ] System tray with usage notifications
- [ ] Cost budgeting and alerts
- [ ] Historical data visualization
- [ ] Auto-update mechanism
- [ ] Plugin system for custom providers
- [ ] Multi-user support with profiles

### 11.2 Performance Improvements
- [ ] Persistent cache (SQLite)
- [ ] WebSocket push updates
- [ ] Parallel provider fetching optimization
- [ ] Lazy loading of provider configs

### 11.3 Platform-Specific
- [ ] macOS menu bar integration
- [ ] Windows toast notifications
- [ ] Linux AppIndicator support

## 12. Deployment

### 12.1 Distribution
- **GitHub Releases**: Pre-built binaries for all platforms
- **Homebrew** (macOS/Linux): `brew install aic-tracker`
- **Scoop** (Windows): `scoop install aic-tracker`
- **Cargo**: `cargo install aic_cli`

### 12.2 Installer Structure
```
aic-tracker/
├── bin/
│   ├── aic_agent
│   ├── aic_app
│   └── aic_cli
├── share/
│   ├── applications/
│   └── icons/
└── scripts/
    └── post-install.sh
```

## 13. Versioning

### 13.1 Version Scheme
- Semantic versioning: `MAJOR.MINOR.PATCH`
- Current: `0.5.0`

### 13.2 Version Locations
- `Cargo.toml` for each crate
- Agent health endpoint
- UI title bar
- CLI `--version` flag

## 14. Development Workflow

### 14.1 Adding a New Provider
1. Create provider module in `aic_core/src/providers/`
2. Implement `ProviderService` trait
3. Add to provider registry
4. Update documentation
5. Add tests with mock responses

### 14.2 Making UI Changes
1. Modify files in `aic_app/src-ui/`
2. Run `validate.ps1` to check syntax
3. Test with `./debug-build.ps1`
4. Verify on target platforms

### 14.3 Release Process
1. Update version in all `Cargo.toml` files
2. Update CHANGELOG.md
3. Create git tag: `git tag v0.6.0`
4. Push tag to trigger CI/CD
5. Verify release artifacts

---

## Appendix A: Environment Variables

| Variable | Description |
|----------|-------------|
| `AIC_AGENT_PORT` | Agent port (default: 8080) |
| `AIC_CONFIG_DIR` | Config directory override |
| `AIC_LOG_LEVEL` | Logging level (trace/debug/info/warn/error) |
| `OPENAI_API_KEY` | OpenAI API key |
| `ANTHROPIC_API_KEY` | Anthropic API key |
| `GOOGLE_API_KEY` | Google AI API key |

## Appendix B: API Examples

### Health Check
```bash
curl http://localhost:8080/health
```

### Get All Usage
```bash
curl http://localhost:8080/api/v1/usage | jq
```

### Update Provider Config
```bash
curl -X POST http://localhost:8080/api/v1/config/openai \
  -H "Content-Type: application/json" \
  -d '{"api_key": "sk-xxx", "enabled": true}'
```
