# Consolidate Auto-Start Settings

## Problem

Monitor auto-start is configured in two places:
1. **Monitor tab**: `AutoStartMonitorCheck` — dead code (XAML only, never wired to logic)
2. **Layout tab**: `StartMonitorWithWindowsCheck` — wired to Windows Registry Run entry

This is confusing and the Monitor tab checkbox does nothing.

## Decision

The UI requires the Monitor to function, so the Monitor should **always** auto-start when the UI launches (`StartMonitorWarmup()` runs unconditionally in `App.xaml.cs:142`). There is no need for a separate Monitor auto-start toggle.

The single "Start UI with Windows" checkbox is sufficient: Windows starts the UI → UI starts the Monitor.

## Changes

### 1. Remove dead `AutoStartMonitorCheck` from Monitor tab

**File:** `AIUsageTracker.UI.Slim/SettingsWindow.xaml` (line 828-830)

Remove the `CheckBox` element from the Monitor tab's Status section.

### 2. Remove "Start Monitor with Windows" from Layout tab

**File:** `AIUsageTracker.UI.Slim/SettingsWindow.xaml` (line 532-535)

Remove `StartMonitorWithWindowsCheck` checkbox from the "Windows Startup" section.

### 3. Update Layout tab section

Rename "Windows Startup" section to reflect it now has a single option. Keep `StartUiWithWindowsCheck`.

### 4. Update `WindowsStartupService.cs`

Remove Monitor-related registry logic (`MonitorValueName`, `GetMonitorExePath()`, Monitor half of `Read()`/`Apply()`). Keep only the UI registry entry.

### 5. Update `SettingsWindow.xaml.cs`

- `PopulateLayoutSettings()`: Remove `StartMonitorWithWindowsCheck` loading, simplify to single UI auto-start read.
- `PersistAllSettingsAsync()`: Remove Monitor auto-start save, simplify to single UI auto-start apply.

### 6. Update `AppPreferences.cs`

Remove `StartMonitorWithWindows` property.

### 7. Update installer (`scripts/setup.iss`)

Remove `startupmonitor` task and its registry entry. Keep only `startuptracker`.

## Files Changed

| File | Change |
|------|--------|
| `SettingsWindow.xaml` | Remove two checkboxes, simplify Layout tab section |
| `SettingsWindow.xaml.cs` | Remove Monitor auto-start load/save |
| `WindowsStartupService.cs` | Remove Monitor registry logic |
| `AppPreferences.cs` | Remove `StartMonitorWithWindows` property |
| `scripts/setup.iss` | Remove `startupmonitor` task and registry entry |

## Migration

`AppPreferences.StartMonitorWithWindows` is removed. On load, the migration logic in `ApplyMigrations` should handle the missing property gracefully (it's a boolean with a default, so this is safe — JSON deserialization will use the default).
