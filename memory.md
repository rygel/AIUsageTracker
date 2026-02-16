# AI Consumption Tracker - Memory State

## Project Overview

A .NET 8.0 WPF application for tracking AI provider usage across multiple services. Uses a background Agent to collect data, with multiple UI frontends (WPF Slim UI, Web UI).

## Architecture

```
┌─────────────────┐     HTTP API      ┌─────────────────┐
│   Slim UI (WPF) │ ◄───────────────► │   Agent (5000)  │
│                 │                   │   SQLite DB     │
└─────────────────┘                   └─────────────────┘
                                              ▲
┌─────────────────┐     Direct DB             │
│    Web UI       │ ◄─────────────────────────┘
│   Port 5100     │
└─────────────────┘
```

## Projects

| Project | Description | Port/Notes |
|---------|-------------|------------|
| **AIConsumptionTracker.Core** | Domain models, interfaces, business logic | PCL |
| **AIConsumptionTracker.Infrastructure** | Providers, external services, configuration | |
| **AIConsumptionTracker.Agent** | Background HTTP service for data collection | Port 5000-5010 |
| **AIConsumptionTracker.Web** | ASP.NET Core Razor Pages web UI | Port 5100 |
| **AIConsumptionTracker.UI.Slim** | Lightweight WPF desktop app | |
| **AIConsumptionTracker.UI** | Full WPF desktop app | |
| **AIConsumptionTracker.CLI** | Console interface | Cross-platform |
| **AIConsumptionTracker.Tests** | Unit tests | xUnit + Moq |

## Current Branch

`feature/web-ui-htmx-themes`

## Recent Work Completed

### Web UI (AIConsumptionTracker.Web)
- ✅ ASP.NET Core Razor Pages application
- ✅ 4 professional themes: Dark, Light, Corporate, Midnight
- ✅ Sidebar navigation with Dashboard, Charts, Providers, History, Raw Data
- ✅ HTMX integration for 60s auto-refresh
- ✅ Chart.js line charts for usage over time
- ✅ Raw database table viewer with pagination (100 rows/page)
- ✅ Start Agent button in sidebar
- ✅ CSP headers configured for Dev/Production

### Agent (AIConsumptionTracker.Agent)
- ✅ HTTP API on port 5000 (auto-discovers 5000-5010)
- ✅ SQLite database with 4 tables: providers, provider_history, raw_snapshots, reset_events
- ✅ Debug mode with `--debug` flag
- ✅ Detailed console logging for data collection
- ✅ Reset event detection for quota-based and usage-based providers
- ✅ Port discovery file: `%LOCALAPPDATA%\AIConsumptionTracker\Agent\agent.port`

### Slim UI (AIConsumptionTracker.UI.Slim)
- ✅ Compact horizontal progress bars
- ✅ System tray integration
- ✅ Multi-tab Settings dialog
- ✅ Web UI launch button (globe icon)
- ✅ Dynamic Agent port discovery

## Database Schema

### providers
- Static provider configuration
- Fields: provider_id, provider_name, payment_type, api_key, auth_source, is_active

### provider_history
- Time-series usage data (kept indefinitely)
- Fields: id, provider_id, usage_percentage, cost_used, cost_limit, is_available, status_message, fetched_at

### raw_snapshots
- Raw JSON data (14-day TTL, auto-cleanup)
- Fields: id, provider_id, raw_json, http_status, fetched_at

### reset_events
- Quota/limit reset tracking (kept indefinitely)
- Fields: id, provider_id, provider_name, previous_usage, new_usage, reset_type, timestamp

## API Endpoints (Agent)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/health` | GET | Health check |
| `/api/usage` | GET | Latest provider usage |
| `/api/usage/{id}` | GET | Single provider usage |
| `/api/config` | GET/POST/DELETE | Provider configurations |
| `/api/preferences` | GET/POST | User preferences |
| `/api/refresh` | POST | Trigger data refresh |
| `/api/history` | GET | Usage history |
| `/api/history/{id}` | GET | Provider-specific history |
| `/api/resets/{id}` | GET | Reset events for provider |
| `/api/scan-keys` | POST | Scan for API keys |

## Web UI Pages

| Page | URL | Description |
|------|-----|-------------|
| Dashboard | `/` | Stats cards, provider usage with progress bars |
| Charts | `/Charts` | Line chart of usage over time |
| Providers | `/Providers` | Table of all providers |
| Provider Details | `/Provider/{id}` | Individual provider history + resets |
| History | `/History` | Complete usage history |
| Raw Data | `/Data/{table}` | Raw database tables with pagination |

## Running the Applications

```bash
# Start Agent (with debug output)
dotnet run --project AIConsumptionTracker.Agent -- --debug

# Start Web UI
dotnet run --project AIConsumptionTracker.Web

# Start Slim UI
dotnet run --project AIConsumptionTracker.UI.Slim
```

## Key Files

### Agent
- `AIConsumptionTracker.Agent/Program.cs` - HTTP API setup, debug mode
- `AIConsumptionTracker.Agent/Services/ProviderRefreshService.cs` - Data collection, reset detection
- `AIConsumptionTracker.Agent/Services/UsageDatabase.cs` - SQLite operations
- `AIConsumptionTracker.Agent/Services/ConfigService.cs` - Configuration management

### Web UI
- `AIConsumptionTracker.Web/Program.cs` - ASP.NET Core setup, CSP, API endpoints
- `AIConsumptionTracker.Web/Services/WebDatabaseService.cs` - Read-only DB access
- `AIConsumptionTracker.Web/Services/AgentProcessService.cs` - Start/check Agent
- `AIConsumptionTracker.Web/Pages/Shared/_Layout.cshtml` - Sidebar layout

### Slim UI
- `AIConsumptionTracker.UI.Slim/MainWindow.xaml` - Main window layout
- `AIConsumptionTracker.UI.Slim/Services/AgentService.cs` - HTTP client for Agent
- `AIConsumptionTracker.UI.Slim/Services/AgentLauncher.cs` - Start Agent process

## Provider Types

### Quota-Based (IsQuotaBased = true)
- Z.AI, Synthetic
- UsagePercentage = % of quota USED
- Progress bar: full green = lots remaining

### Credits-Based
- OpenCode
- UsagePercentage = % of credits used

### Usage-Based
- OpenAI, Anthropic, Gemini, etc.
- UsagePercentage = calculated from spending
- Progress bar: full red = high spending

## Reset Detection Logic

### Quota-Based Providers
- Detect when UsagePercentage drops from >50% to <30% of previous
- Example: 80% → 15% = quota reset

### Usage-Based Providers
- Detect when CostUsed drops by >20%
- Example: $100 → $20 = usage reset

## Known Issues

1. **OpenCodeProvider** - Returns "None" instead of JSON with invalid API key (handled gracefully)
2. **LSP Errors** - Pre-existing LSP errors in UI.Slim (SharpVectors, StatusText) - build succeeds

## Configuration

- Config stored in: `%LOCALAPPDATA%\AIConsumptionTracker\auth.json`
- Agent port file: `%LOCALAPPDATA%\AIConsumptionTracker\Agent\agent.port`
- Database: `%LOCALAPPDATA%\AIConsumptionTracker\Agent\usage.db`

## Next Steps / TODO

- [ ] Test full end-to-end flow with all providers
- [ ] Verify reset events are being stored correctly
- [ ] Add more providers as needed
- [ ] Consider adding alerts/notifications for high usage
