# Keyboard Shortcuts & Theme System

## Summary of Changes

Added two new features to AI Consumption Tracker:

### 1. Keyboard Shortcuts

| Shortcut | Action | Description |
|---------|---------|----------|
| **Ctrl+R** | Refresh Data | Forces an immediate refresh of all provider data |
| **Ctrl+P** | Toggle Privacy Mode | Switches between showing/hiding sensitive information |
| **Ctrl+T** | Toggle Theme | Switches between Dark and Light theme |
| **Ctrl+Q** | Close App | Closes the main window |
| **F2** | Open Settings | Opens the provider settings window |
| **Escape** | Close Window | Closes the main window |

**Implementation Details:**
- Added OnKeyDown event handler in MainWindow constructor
- All shortcuts use ModifierKeys.Control for consistency
- Escape and F2 work without modifiers (standard Windows patterns)
- Each shortcut calls existing event handlers
- Ctrl+T calls new ThemeBtn_Click method

### 2. Theme System (Dark/Light)

Added a complete theme system with two modes:

**Dark Theme** (default):
- Window background: Dark gray (#1E1E1E)
- Header/Footer: Darker gray (#252526)
- Text: White
- Icon: 🌙

**Light Theme**:
- Window background: Light gray (#F3F3F3)
- Header/Footer: Medium gray (#E6E6E6)
- Text: Black
- Icon: ☀️

**Features:**
- Theme toggle button in header (🌙/☀️)
- Theme persists to preferences.json
- Applies to all UI elements (window, borders, buttons, scrollbars)
- Instant switching with no flickering
- Proper scrollbar styles for both themes

**Theme Selection:**
- AppTheme.Dark (default)
- AppTheme.Light

**Implementation Files:**
- AppPreferences.cs: Added AppTheme enum and property
- MainWindow.xaml.cs: Added OnKeyDown handler and ThemeBtn_Click method
- MainWindow.xaml: Added ThemeBtn, HeaderBorder and FooterBorder naming
- App.xaml: Added Light scrollbar styles

### 3. Settings - Start with Windows

Added Windows Auto-Startup functionality:

**Files Modified:**
- AppPreferences.cs: Added StartWithWindows property
- SettingsWindow.xaml.cs: Added checkbox in PopulateLayout
- App.xaml.cs: Added SetStartupTaskAsync method
- FEATURE_WINDOWS_STARTUP.md: Complete documentation

**Functionality:**
- Windows Registry integration for auto-start on login
- Graceful error handling
- Platform-specific (Windows only)

**How to Use:**
1. Open Settings
2. Check "Start with Windows"
3. Click Save
4. Close app
5. Restart PC

## Testing Instructions

**Keyboard Shortcuts:**
- Refresh: Ctrl+R forces immediate refresh
- Privacy: Ctrl+P toggles privacy mode
- Theme: Ctrl+T switches theme
- Settings: F2 opens settings window
- Escape closes the main window
ENDOFF'
