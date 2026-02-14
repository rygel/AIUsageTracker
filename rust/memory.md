# AI Consumption Tracker - Rust Development Notes

## Accomplished

### Provider Fixes
- Fixed Antigravity provider categorization (was showing in wrong category when not running)
- Fixed GitHub Copilot provider categorization (was showing in wrong category when not authenticated)
- Added raw response capture to: Synthetic, Z.AI, GitHub Copilot (previously only OpenAI and GenericPayAsYouGo)

### UI Improvements
- Added async loading to main UI and settings (show data immediately, update when fresh)
- Added Cached/Live badge to main window header
- Added Cached/Live badge to settings window header
- Unified badge management: Rust backend acts as the single source of truth for "Live" status
- Badge sync via Tauri events (`data-status-changed` event) and manual querying
- Standardized IPC calls in `index.html` via a centralized `invoke` helper to prevent reference errors
- Fixed agent version/path overwriting issue in settings dialog

### Performance & Caching
- Added timing logs throughout the stack
- Added file logging with 30-day retention to `%LOCALAPPDATA%\ai-consumption-tracker\logs\`
- Added localStorage caching for instant display on startup
- Added agent warm-up/prefetch on startup

### Missing Commands Fixed
- Added `get_historical_usage_from_agent` command
- Added `get_raw_responses_from_agent` command

### Icons
- Copied C# app icons (ico and png) to Rust app icons folder

## Known Issues

### Badge Sync [FIXED]
- Fixed timing issues where the "Live" badge appeared before the UI finished rendering fresh data.
- Fixed `localStorage` key mismatch (`cached_usage_data` vs `usage_data`) and removed hardcoded "Cached" labels in `settings.html`.
- Added `tauri://focus` listener to `settings.html` to force-sync the badge status when the dialog is brought to the foreground.
- Improved reliability by re-ordering initialization logic and fixing a script crash (duplicate `invoke` declaration).

### Icons
- Icons may not be loading correctly in the UI (path issues)

### Raw Logs
- Raw logs may not appear in settings dialog even for providers that capture raw responses

### Tray Icon
- Tray icon cleanup on force-kill not possible (Windows limitation - no signal sent to app)

## Raw Response Providers
Only these providers capture raw HTTP responses:
- OpenAI
- GenericPayAsYouGo
- Synthetic
- Z.AI
- GitHub Copilot
