# AI Consumption Tracker - Rust Development Notes

## egui Desktop Application (NEW)

The egui desktop application (`aic_app_egui`) is a native Rust alternative to the Tauri-based UI.

### Completed Features
- [x] System tray support (minimize to tray, show/quit)
- [x] Provider usage display with progress bars
- [x] Plans & Quotas / Pay As You Go grouping (collapsible)
- [x] Sub-provider support (antigravity details with expandable bars)
- [x] Alphabetical sorting of providers
- [x] Non-blocking API calls with incremental cache updates (polls every 2s)
- [x] SVG icon loading via resvg crate with fallback to letter icons
- [x] Settings dialog with 6 tabs (Providers, Layout, Updates, History, Fonts, Agent)
- [x] GitHub Copilot OAuth device flow
- [x] Auto-start agent on launch option
- [x] Provider filtering (only show available providers)
- [x] "Connected" status display for Status-type providers (Mistral, OpenAI, Anthropic)
- [x] Invert progress bar toggle (shows remaining % instead of used %)
- [x] Hand cursor on hover for clickable elements (providers, groups, sub-providers)
- [x] Improved Layout tab with sections, cards, and better spacing

### Key Technical Decisions
- Uses `eframe` 0.29 with `glow` backend
- Settings window as separate viewport (not embedded)
- Non-blocking agent API returns cached data immediately
- Background refresh updates cache incrementally
- Progress bars show REMAINING percentage by default (full bar = plenty remaining, like a battery)
- Invert mode: displays USED percentage (bar fills as you use more)
- Color always reflects actual usage (high usage = red, regardless of bar display)
- Sub-provider bars match main bar height (24px)

### Color Scheme
- Background: `#1E1E1E` (30, 30, 30) - darker
- Panel fill: `#252526` (37, 37, 38)
- Card background: `#232323` (35, 35, 35)
- Button background: `#444444` (68, 68, 68)
- Green (good): `#3CB371` (60, 179, 113)
- Yellow (warning): `#FFD700` (255, 215, 0)
- Red (critical): `#DC143C` (220, 20, 60)

### Footer Controls (Main UI)
- Privacy button (🔒/🔓) - toggles privacy mode to hide sensitive data
- Invert button (⇤/⇥) - toggles progress bar between remaining%/used%
- Agent status badge (green/yellow/red LED indicator)
- Time since last refresh
- Settings, Refresh, and Agent buttons

### Layout Tab Improvements
- Organized into 3 sections: Display Options, Automation, Color Thresholds
- Each section in a framed card with rounded corners
- More spacing between items (8-12px)
- Larger text (12-13pt)
- Cleaner slider labels with % suffix

### Recent egui Changes
- Added minimax-io provider support (MiniMax International)
- Removed "Show All" checkbox from footer (still in Layout tab)
- Removed "Enter API key" placeholder text (less clutter)
- Made main window background darker
- Status-type providers now show "Connected" instead of "OK"

---

## Tauri Application

### Accomplished

#### Frontend Refactoring
- Separated frontend assets from `src/` to new `www/` directory
- Updated `tauri.conf.json` to use `www` as `frontendDist`
- Added HTMX library (v2.0.3) with configuration for future component-based UI
- Created `htmx-components.js` with reusable HTMX components
- Created `htmx-config.js` with HTMX configuration and Tauri extension

#### Discoveries
- HTMX doesn't natively support `javascript:` URLs - attempts to use `hx-get="javascript:HTMXProviders.load()"` failed with "htmx:invalidPath" error
- The htmx.min.js file became corrupted when fetched locally - fixed by loading from CDN
- Tauri serves files from the directory specified in `tauri.conf.json` under `build.frontendDist`

#### CSS Extraction
- Created `css/main.css` with shared styles, CSS variables, scrollbars, base styles
- Created `css/index.css` for main window styles
- Created `css/settings.css` for settings window styles  
- Created `css/info.css` for info window styles

#### JavaScript Extraction
- Created `js/utils.js` with shared utilities (invoke helper, cache functions, escapeHtml, formatResetDisplay)
- Created `js/index.js` for main window logic (data loading, rendering, agent management)
- Created `js/settings.js` for settings window logic (tabs, providers, history, save settings)
- Created `js/info.js` for info window logic (system info display)

#### Provider Fixes
- Fixed Antigravity provider categorization (was showing in wrong category when not running)
- Fixed GitHub Copilot provider categorization (was showing in wrong category when not authenticated)
- Added raw response capture to: Synthetic, Z.AI, GitHub Copilot (previously only OpenAI and GenericPayAsYouGo)
- Filtered providers: only show providers with API keys in main UI (except GitHub Copilot if authenticated)
- Fixed antigravity CSRF token regex: changed `[=\s]+` to `[= ]+` (literal space)

#### UI Improvements
- Added async loading to main UI and settings (show data immediately, update when fresh)
- Added Cached/Live badge to main window header
- Added Cached/Live badge to settings window header
- Unified badge management: Rust backend acts as the single source of truth for "Live" status
- Badge sync via Tauri events (`data-status-changed` event) and manual querying
- Standardized IPC calls in `index.html` via a centralized `invoke` helper to prevent reference errors
- Fixed agent version/path overwriting issue in settings dialog
- Privacy mode: fixed Antigravity username not being masked in settings dialog
- Removed API key input box for Antigravity (shows connection status instead)
- Added more fonts to font selection (20+ fonts including sans-serif, serif, monospace)
- Applied font settings (family, size, bold, italic) to main UI content and bars

#### Performance & Caching
- Added timing logs throughout the stack
- Added file logging with 30-day retention to `%LOCALAPPDATA%\ai-consumption-tracker\logs\`
- Added localStorage caching for instant display on startup
- Added agent warm-up/prefetch on startup
- Non-blocking API: returns cached data immediately, updates incrementally

#### Missing Commands Fixed
- Added `get_historical_usage_from_agent` command
- Added `get_raw_responses_from_agent` command

#### Icons
- Copied C# app icons (ico and png) to Rust app icons

### Known Issues

#### Tray Icon
- Tray icon cleanup on force-kill not possible (Windows limitation - no signal sent to app)

### Raw Response Providers
Only these providers capture raw HTTP responses:
- OpenAI
- GenericPayAsYouGo
- Synthetic
- Z.AI
- GitHub Copilot

---

## Provider Configuration

### MiniMax Providers
Two separate MiniMax providers are supported:
- `minimax` - MiniMax (China) - uses `MINIMAX_API_KEY` env var, endpoint: `api.minimax.chat`
- `minimax-io` - MiniMax (International) - uses `MINIMAX_IO_API_KEY` env var, endpoint: `api.minimax.io`

Both are registered in well-known providers list for easy configuration.
