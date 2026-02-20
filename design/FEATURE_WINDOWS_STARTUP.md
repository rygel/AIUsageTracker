# Keyboard Shortcuts & Windows Auto-Startup

## Summary of Changes

Added two major new features to AI Consumption Tracker:

### 1. Keyboard Shortcuts

| Shortcut | Action | Description |
|---------|---------|----------|
| **Ctrl+R** | Refresh Data | Forces an immediate refresh of all provider data |
| **Ctrl+P** | Toggle Privacy Mode | Switches between showing/hiding sensitive information |
| **Ctrl+T** | Toggle Theme | Switches between Dark and Light theme |
| **Ctrl+Q** | Close Application | Closes the main window |
| **F2** | Open Settings | Opens the provider settings window |
| **Escape** | Close Window | Closes the main window |

**Implementation Details:**
- Added `OnKeyDown` event handler in MainWindow constructor
- All shortcuts use `ModifierKeys.Control` for consistency
- Escape and F2 work without modifiers (standard Windows patterns)
- Each shortcut calls existing event handlers to maintain code reuse

### 2. Windows Auto-Startup

**Settings UI:**
- Added checkbox "Start with Windows" in Layout tab
  ToolTip: "Add application to Windows Startup (Ctrl+Alt+R will restart)"
  Toggles automatic startup on Windows login

**Implementation Details:**
- New property in `AppPreferences`: `public bool StartWithWindows { get; set; } = false;`
- Added checkbox control in `SettingsWindow.PopulateLayout()`
- Event handlers save preference when toggled
- Saves to preferences.json automatically

**Technical Notes:**

**Registry Path:**
- **Registry Key**: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
- **Registry Value Name**: "AI Consumption Tracker" (app name)
- **Registry Value**: Full path to executable
- **Value Type**: String

**Functionality:**
1. Check "Start with Windows" in settings
2. Next time you log in to Windows, app launches automatically
3. App appears in Task Manager > Startup tab
4. Option to restart app after changing preference

**When Enabled:**
1. Uncheck "Start with Windows" in settings
2. App will not auto-start on login
3. App must be launched manually

**User Experience:**
- Check → Save → Restart: Users learn setting effect
- Settings dialog opens automatically
- Changes save instantly

**Platform Compatibility:**
- Only works on Windows (WPF/Windows-only platform)
- Does not affect Linux CLI or other platforms
- Registry settings are per-user (HKCU), not system-wide

**Security Considerations:**
- Registry writes require appropriate permissions (may fail if running with limited privileges)
- Command path includes quotes to handle paths with spaces
- Exception handling prevents app crashes from registry access failures

### Dependencies:**
- `Microsoft.Win32.Registry` - Windows native registry API
- `System.Runtime.InteropServices` - P/Invoke support for registry access

### Future Enhancements:**
- **Auto-start delay**: Add option to delay startup (e.g., 5 seconds, 30 seconds)
- **Startup arguments**: Support command-line args when started automatically
- **Conditional startup**: Only start if certain conditions met (e.g., specific time of day)
- **Multiple profiles**: Support different startup configurations
- **Run as administrator**: Option to request elevation if needed
- **Linux/Mac support**: Platform-specific startup (LaunchAgent on Linux/Mac)

### Related Files:
1. `AIConsumptionTracker.Core/Models/AppPreferences.cs` - Added AppTheme enum and StartWithWindows property
2. `AIConsumptionTracker.UI.Slim/App.xaml.cs` - Modified OnStartup method to handle Windows Auto-Startup
3. `AIConsumptionTracker.UI.Slim/SettingsWindow.xaml.cs` - Added checkbox in PopulateLayout() and event handlers
4. `AIConsumptionTracker.UI.Slim/App.xaml.cs` - Added SetStartupTaskAsync() method for registry operations
5. `AIConsumptionTracker.UI.Slim/App.xaml.cs` - Added Windows registry using statements and proper command quoting

## Design Document Created: FEATURE_WINDOWS_STARTUP.md

Complete documentation for Windows Auto-Startup and keyboard shortcuts system.

## Related Shortcut

The "Start with Windows" setting pairs well with keyboard shortcut implementation:
- **Ctrl+Alt+R**: Shows "Restart required" notification when setting is enabled
- Users learn: Check box → Save → Close → Restart to take effect

### Testing Instructions

Test functionality:
1. Open Settings
2. Check "Start with Windows" checkbox
3. Click Save
4. Close settings
5. Log in to Windows (Task Manager > Startup tab)
6. Restart computer

**Verify Registry:**
1. Open Registry Editor: `regedit`
2. Navigate to: `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`
3. Verify "AI Consumption Tracker" entry exists with path to exe

**Test Disable:**
1. Uncheck "Start with Windows" checkbox
2. Save changes
3. Restart computer

**Expected Behavior:**
- App launches automatically after Windows login
- App appears in Task Manager > Startup tab
- Option to restart app after changing preference
- Checkbox state persists across app restarts
- Registry entry is created/removed on save
- No performance impact on normal usage

## User Experience

**When Enabled:**
- Check "Start with Windows" in settings
2. Save changes
3. App launches automatically
4. Next time you log in to Windows, app launches automatically
5. App appears in Task Manager > Startup tab
6. Option to restart app after changing preference
7. Settings saved immediately when toggled

**When Disabled:**
- Uncheck "Start with Windows" in settings
2. Save changes
3. App will not auto-start on login
4. App must be launched manually

### Technical Notes

### Registry Access
- Uses `Microsoft.Win32.Registry` namespace
- `CurrentUser.OpenSubKey` for current user scope
- Requires `Microsoft.Win32.Registry` and `using System.Runtime.InteropServices;`
- Graceful error handling - doesn't fail app if registry operations fail
- Command path includes quotes to handle paths with spaces
- Exception handling prevents app crashes from registry access failures

### Platform Compatibility
- Only works on Windows (WPF/Windows-only platform)
- Does not affect Linux CLI or other platforms
- Registry settings are per-user (HKCU), not system-wide

### Security Considerations
- Registry writes require appropriate permissions (may fail if running with limited privileges)
- Command path includes quotes to handle paths with spaces
- Exception handling prevents app crashes from registry access failures

### Limitations
- User must have administrative privileges OR write access to registry
- Some corporate environments may restrict registry writes
- Feature may not work on certain Windows configurations

### Dependencies
- `Microsoft.Win32.Registry` - Windows native registry API
- `System.Runtime.InteropServices` - P/Invoke support for registry access

### Future Enhancements

Potential improvements:
1. **Auto-start delay**: Add option to delay startup (e.g., 5 seconds, 30 seconds)
2. **Startup arguments**: Support command-line args when started automatically
3. **Conditional startup**: Only start if certain conditions met (e.g., specific time of day)
4. **Multiple profiles**: Support different startup configurations
5. **Run as administrator**: Option to request elevation if needed
6. **Linux/Mac support**: Platform-specific startup (LaunchAgent on Linux/Mac)

## Testing Instructions

**Test Windows Startup:**
1. Open Settings window
2. Check "Start with Windows" checkbox
3. Click Save
4. Close settings
5. Log in to Windows

**Verify Registry:**
1. Open Registry Editor: `regedit`
2. Navigate to: `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`
3. Verify "AI Consumption Tracker" entry exists with path to exe

**Test Disable:**
1. Uncheck "Start with Windows" checkbox
2. Save changes
3. Restart computer

**Expected Behavior:**
- App launches automatically after Windows login
- App appears in Task Manager > Startup tab
- Option to restart app after changing preference
- Checkbox state persists across app restarts
- Registry entry is created/removed on save
- No performance impact on normal usage

**Registry Path:**
- **Registry Key**: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`
- **Registry Value Name**: "AI Consumption Tracker" (app name)
- **Registry Value**: Full path to executable
- **Value Type**: String
