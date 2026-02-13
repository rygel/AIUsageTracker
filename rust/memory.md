# Development Memory - Rust AI Consumption Tracker

## Current Work Summary

### Objective
Building a Rust-based cross-platform replacement for the .NET WPF AI Consumption Tracker, with better performance and cross-platform support.

### Architecture
- **aic_core**: Core library with provider implementations and business logic
- **aic_agent**: Background HTTP service (port 8080) that fetches usage data from AI providers
- **aic_app**: Tauri-based desktop application (Windows/Linux/Mac)
- **aic_cli**: Command-line interface
- **aic_web**: Web dashboard for browser access

## Recent Implementations (2026-02-13)

### 1. Collapsible Provider Groups
- **Plans & Quotas** and **Pay As You Go** groups are now collapsible
- Click the group header to expand/collapse all providers in that group
- Visual indicator: ▼ when expanded, ▶ when collapsed
- State persisted in localStorage (restored on app restart)
- Hover effects on headers for better UX

### 2. Collapsible Sub-Providers
- Providers with sub-providers (like Antigravity) show a collapsible header
- Displays count: "3 sub providers"
- Each provider remembers its collapsed state individually
- Works in both compact and standard view modes
- Click to toggle visibility of sub-provider bars

### 3. Inverted Progress Bars
- Settings option to invert progress bars (show remaining instead of used)
- Applied to both main provider bars and sub-provider bars
- Affects all progress bar calculations throughout the UI
- State persisted in preferences

### 4. Privacy Icon Updates
- Changed from 👁/🙈 to 🔒 (lock icon) to match C# version
- Privacy mode active: button turns gold (#FFD700)
- Consistent across all windows (main, settings, info)
- No icon change, only color change indicates state

### 5. Agent Control Button
- 🤖 button now toggles between start and stop
- **Running**: Shows "Agent Running - Click to Stop"
- **Stopped**: Shows "Start Agent"
- Stopping agent clears the providers list
- Starting agent triggers status check after 2 seconds

### 6. Window Close Behavior
- **Main window**: Hides instead of closing (app runs in tray)
- **Settings window**: Hides instead of closing
- **Info window**: Hides instead of closing
- Use tray menu "Exit" to fully quit application
- All windows can be reopened from tray menu

### 7. Simplified Tray Menu
- Reduced to essential items:
  - **Show**: Bring main window to front
  - **Info**: Open about dialog
  - **Exit**: Quit application
- Removed: Settings, Refresh, Auto Refresh, Start/Stop Agent
- All functionality accessible through UI buttons

### 8. Agent-Only Configuration (CRITICAL)
**All configuration operations now go through agent API:**
- `GET /api/providers/discovered` - Read providers
- `PUT /api/providers/{id}` - Save single provider
- `DELETE /api/providers/{id}` - Remove provider
- `POST /api/config/providers` - Save all providers
- `POST /api/discover` - Trigger discovery scan

**Removed from UI app:**
- Direct auth.json loading on startup
- Direct file access in commands
- `ConfigLoader` usage in UI layer

**Agent endpoints added:**
- `PUT /api/providers/:id` - Save provider config
- `DELETE /api/providers/:id` - Remove provider
- `POST /api/config/providers` - Bulk save
- `POST /api/discover` - Trigger discovery

### 9. Settings Data Preloading
- Main window preloads settings data after loading its own data
- Settings window checks for preloaded data first
- If preloaded data exists, settings open instantly
- Falls back to agent API if no preloaded data
- Preloading happens in background after each refresh

### 10. UI/UX Improvements
- **Group headers**: Hover effects and visual feedback
- **Sub-provider headers**: "X sub providers" text with toggle arrow
- **Privacy button**: Gold color when active (consistent with C#)
- **Agent button**: Enabled when running (allows stopping)
- **Window focus**: Main window reloads preferences on focus (syncs with settings changes)

## Known Issues

1. **Port Conflicts**: Agent sometimes fails to start if port 8080 is in use
2. **Window Close Warnings**: Some harmless Win32 errors when closing application
4. **Antigravity Details**: Requires running VS Code extension to show sub-model data
5. **JavaScript Syntax**: Potential syntax error in index.html (under investigation)

## Next Steps

1. Debug JavaScript syntax error in index.html
2. Test all provider configuration flows through agent API
3. Verify agent start/stop functionality on all platforms
4. Add proper error handling for network failures
5. Implement auto-update mechanism
6. Test cross-platform builds (Linux/Mac)
7. Add click-to-cycle reset display modes

## Development Commands

```bash
# Build everything
cd rust && cargo build --release

# Start agent
cd rust && ./target/release/aic_agent.exe

# Build and run UI
cd rust/scripts && ./debug-build.ps1

# Check for JS syntax errors
cd rust/aic_app/src && node --check index.html 2>&1 || echo "Check failed"

# Test agent diagnostics
cd rust/scripts && ./test-agent.ps1
```

## Critical Design Principles

### Agent-Only Architecture
**The UI app must NEVER read auth.json or any configuration files directly.** All configuration operations must go through the agent's REST API:

- **Reading providers**: Use `GET /api/providers/discovered`
- **Saving a provider**: Use `PUT /api/providers/{id}`
- **Removing a provider**: Use `DELETE /api/providers/{id}`
- **Saving all providers**: Use `POST /api/config/providers`
- **Triggering discovery**: Use `POST /api/discover`
- **Getting usage**: Use `GET /api/providers/usage`
- **Refreshing usage**: Use `POST /api/providers/usage/refresh`

**Why this matters:**
- Single source of truth: The agent manages all provider configurations
- Consistency: All windows get the same data from the agent
- Security: The agent can implement additional validation and security
- Flexibility: The agent can discover providers from multiple sources (env vars, config files, etc.)

**What was removed:**
- Direct auth.json loading on startup (removed from main.rs setup)
- Direct file access in save/remove provider commands (routed through agent API)
- `ConfigLoader::load_config()` calls in UI (replaced with agent API calls)

**Current State:**
- The agent is the only component that reads/writes auth.json
- The UI app communicates with agent via HTTP on port 8080
- All provider data flows: Agent (source of truth) → UI (display only)

## File Locations

- **Main UI**: `aic_app/src/index.html`
- **Settings**: `aic_app/src/settings.html`
- **Info/About**: `aic_app/src/info.html`
- **Agent**: `aic_agent/src/main.rs`
- **Core**: `aic_core/src/`
- **Config**: Agent reads from `~/.ai-consumption-tracker/auth.json`

## Architecture Flow

```
┌─────────────┐      HTTP      ┌──────────────┐      HTTP      ┌─────────────────┐
│   aic_app   │ ◄─────────────► │  aic_agent   │ ◄─────────────► │ AI Provider APIs│
│  (Tauri UI) │   Port 8080     │  (HTTP API)  │                 │  (OpenAI, etc.) │
└─────────────┘                 └──────────────┘                 └─────────────────┘
       │                               │
       │                               │
       ▼                               ▼
  LocalStorage                    auth.json
  (UI preferences)           (Provider configs)
```

## Current Branch

**Branch**: `feature/libsql-upgrade`
**Commits**: Multiple commits with collapsible sections, agent API routing, stop agent button, and UI improvements
**Status**: Ready for testing, pending JavaScript syntax error resolution
