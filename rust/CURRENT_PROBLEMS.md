# Current Problems and Recent Fixes

## Recent Work (February 11, 2026)

### Feature: Agent Connection Status Indicator

**Added:** Real-time status indicator in the footer showing agent connection state.

**UI Changes:**
- Added colored status dot in footer between "Show All" checkbox and action buttons
- Green dot with glow effect: Agent is connected and running
- Red dot: Agent is disconnected/not running
- Yellow pulsing dot: Currently connecting/checking
- Status text: "Agent Connected", "Agent Disconnected", or "Connection Error"

**Implementation:**
- Updated `checkAgentStatus()` function to also update status indicator
- Status checks immediately on page load and every 5 seconds
- Works with existing `get_agent_status_details` Tauri command

**CSS Classes:**
- `.agent-status` - Container for indicator and text
- `.status-indicator` - Colored dot
- `.status-indicator.connected` - Green with glow
- `.status-indicator.disconnected` - Red
- `.status-indicator.connecting` - Yellow with pulse animation

---

### Improvement: Smart Build Script

**Change:** Updated `debug-build.ps1` to intelligently decide whether to build or run.

**Previous Behavior:**
- Always ran `cargo tauri build` before running the app
- Slow development cycle even for minor changes

**New Behavior:**
- Checks if executable exists at `target/debug/aic_app(.exe)`
- Compares modification times of all source files (`.rs`, `.toml`, `.html`, `.css`, `.js`) against the executable
- Only rebuilds when source files are newer than the executable
- Runs the existing executable directly if no changes detected
- Added `-ForceBuild` flag to override and force rebuild

**Usage:**
```powershell
cd rust
.\debug-build.ps1          # Smart build - only builds if needed
.\debug-build.ps1 -ForceBuild  # Force rebuild
.\debug-build.ps1 -Help     # Show help
```

---

### Issue: Compilation Errors in `aic_app`

**Problem:** The `aic_app` binary failed to compile due to missing Tauri command implementations that were referenced in `main.rs` but not defined.

**Missing Commands:**
- `open_settings_window` - Command to open the settings window
- `start_agent` - Command to start the agent process
- `stop_agent` - Command to stop the agent process
- `is_agent_running` - Command to check if agent is running

**Error Messages:**
```
error: cannot find macro `__cmd__open_settings_window` in this scope
error: cannot find macro `__cmd__start_agent` in this scope
error: cannot find macro `__cmd__stop_agent` in this scope
error: cannot find macro `__cmd__is_agent_running` in this scope
```

### Solution Implemented

Added the missing commands to `rust/aic_app/src/main.rs`:

1. **`open_settings_window`** - Opens the settings webview window and focuses it
2. **`start_agent`** - Wrapper function that calls the existing `start_agent_internal` to start the agent process
3. **`stop_agent`** - Stops the running agent process by killing it
4. **`is_agent_running`** - Checks the status of the agent process and returns whether it's running

### Additional Fixes

**Type Mismatch in `get_agent_status_details`:**
- Changed `process_id: pid` to `process_id: Some(pid)` to match the `Option<u32>` type

**Borrowing Issue in `check_and_update_tray_status`:**
- Fixed by storing the state reference before locking: `let state = app_handle.state::<AppState>();`

### Current Status

✅ **Build Status:** `aic_app` now compiles successfully with only minor warnings (unused imports, unused fields)

---

## Known Issues

### Window Close Error (Chrome Warning)

**Error Message:**
```
Close window command received
Exiting application
[0211/182950.644:ERROR:ui\gfx\win\window_impl.cc:124] Failed to unregister class Chrome_WidgetWin_0. Error = 1412
```

**Analysis:**
- Error 1412 (`ERROR_CLASS_ALREADY_EXISTS`) is a benign Chromium/Tauri warning on Windows
- The application closes correctly; this is just a cleanup complaint from the WebView
- Common issue with Tauri apps on Windows when closing windows

**Status:** Not critical - documented for future investigation if needed

---

## Build Warnings

The following warnings are present but do not prevent compilation:

### aic_core
- Unused import: `warn` in `config.rs`
- Unused import: `Serialize` in `github_auth.rs`
- Unused import: `Serialize` in `gemini.rs`
- Unused variable: `message` in `cloud_code.rs`
- Unused variable: `reset_str` in `generic_payg.rs`
- Unused field: `client` in `ConfigLoader`
- Unused field: `reset_time` in `KimiUsageData`
- Unused field: `remaining` in `ZaiQuotaLimitItem`

### aic_app
- Unused variable: `state` in `commands.rs` (line 73)
- Unused import: `WebviewWindowBuilder` in `main.rs`
- Unused fields: `user_code`, `verification_uri`, `interval` in `DeviceFlowState`

**Note:** These can be cleaned up using `cargo fix` if desired.
