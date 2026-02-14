# AI Consumption Tracker - Rust Port with Tauri

This is a Rust port of the AI Consumption Tracker application, originally written in C# with WPF.

## Project Structure

```
rust/
├── Cargo.toml              # Workspace configuration
├── aic_core/               # Core library with models and providers
├── aic_cli/                # Command-line interface
└── aic_app/                # Tauri desktop application
```

## Ported Components

### Core Models (aic_core/src/models.rs)
- PaymentType enum with serde serialization
- ProviderUsage, ProviderUsageDetail structs
- ProviderConfig struct with full configuration support
- AppPreferences struct for UI settings

### Provider System (aic_core/src/providers.rs)
- Provider trait using async-trait
- OpenAIProvider: API key validation
- AnthropicProvider: Basic connectivity
- DeepSeekProvider: Balance fetching

### Configuration (aic_core/src/config.rs)
- ConfigLoader: Reads auth.json from multiple locations
- TokenDiscoveryService: Environment variables, Kilo Code secrets
- ProviderManager: Async parallel fetching with semaphore control

### CLI (aic_cli/src/main.rs)
- status command with table/json output
- list command for configured providers
- auth command stub for GitHub

### Tauri App (aic_app/)
- Modern dark theme UI
- Real-time provider cards with progress bars
- Refresh and filter functionality

## Building

```bash
cd rust
cargo build --release

# Run CLI
cargo run -p aic_cli -- status

# Run Tauri app
cargo run -p aic_app
```

## Logging

Log files are written to:
- **App**: `%LOCALAPPDATA%\ai-consumption-tracker\logs\app.log`
- **Agent**: `%LOCALAPPDATA%\ai-consumption-tracker\logs\agent.log`

Log files are automatically cleaned up, keeping only the last 30 days.

To enable debug logging, run with the `--debug` flag:
```bash
cargo run -p aic_app -- --debug
```

## Key Differences from C#

1. Native async/await without Task overhead
2. Result-based error handling (no exceptions)
3. Memory safety with Option<T> and lifetimes
4. Cross-platform without .NET runtime
5. Smaller binary size

## Configuration Compatibility

Reads the same files as C# version:
- ~/.ai-consumption-tracker/auth.json
- Environment variables
- Kilo Code secrets

This ensures seamless migration for existing users.
