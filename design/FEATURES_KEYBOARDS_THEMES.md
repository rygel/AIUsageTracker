# Keyboard Shortcuts & Theme System

## Summary of Changes

Added two new features to AI Consumption Tracker:

### 1. Keyboard Shortcuts

| Shortcut | Action | Description |
|---------|---------|-------------|
| **Ctrl+R** | Refresh Data | Forces an immediate refresh of all provider data |
| **Ctrl+P** | Toggle Privacy Mode | Switches between showing/hiding sensitive information |
| **Ctrl+T** | Toggle Theme | Switches between Dark and Light theme |
| **Ctrl+Q** | Close Application | Closes the main window |
| **F2** | Open Settings | Opens the provider settings window |
| **Escape** | Close Window | Closes the main window |

**Implementation Details:**
- Added `OnKeyDown` event handler in `MainWindow` constructor
- All shortcuts use `ModifierKeys.Control` for consistency
- Escape and F2 work without modifiers (standard Windows patterns)
- Each shortcut calls existing event handlers to maintain code reuse

### 2. Theme System (Slim + Web parity)

Theme support is aligned across Slim UI and Web UI with a shared catalog.

#### Available Themes

| Theme | Category |
|-------|----------|
| Dark | Core |
| Light | Core |
| Corporate | Core |
| Midnight | Core |
| Dracula | VS Code popular |
| Nord | VS Code popular |
| Monokai | VS Code popular |
| One Dark | VS Code popular |
| Solarized Dark | VS Code popular |
| Solarized Light | Bright |
| Catppuccin Latte | Pastel (official) |
| Catppuccin Frappe | Pastel (official) |
| Catppuccin Macchiato | Pastel (official) |
| Catppuccin Mocha | Pastel (official) |

Catppuccin values are mapped from the official palette: `https://catppuccin.com/palette/`.

Theme metadata is centralized in `design/theme-catalog.json` (enum names, labels, web keys, representative token checks).
CI contract validation uses this manifest to keep Slim and Web theme catalogs aligned.
Web-generated lists are synced from this manifest via `scripts/sync-theme-catalog.ps1`.

#### Dark Theme (Default)
- Window background: `#1E1E1E` (dark gray)
- Header/Footer background: `#252526` (darker gray)
- Window foreground: `White`
- Scrollbars: Dark gray background with lighter thumb

#### Light Theme
- Window background: `#F3F3F3` (light gray)
- Header/Footer background: `#E6E6E6` (medium gray)
- Window foreground: `Black`
- Scrollbars: Light background with dark thumb

**Theme Toggle Button:**
- Added 🌙/☀️ button in header
- 🌙 = Light mode (click to switch to light)
- ☀️ = Dark mode (click to switch to dark)
- ToolTip: "Toggle Theme (Ctrl+T)"

**Data Persistence:**
- Added `Theme` property to `AppPreferences` model
- Default value: `AppTheme.Dark`
- Slim UI theme is persisted in `%LOCALAPPDATA%\AIUsageTracker\UI.Slim\preferences.json`
- Theme selection in Slim settings is saved immediately on change and also on Save
- Applied on startup (safe default first, then persisted preference)
- Web UI theme is persisted separately in browser `localStorage` (`theme` key)

**Runtime Behavior:**
- Startup applies a safe default theme first, then loads saved preferences asynchronously.
- Theme changes are applied through shared dynamic resources.
- Slim selector uses user-friendly labels (for example `One Dark`, `Catppuccin Macchiato`).
- Web selector and JS allowlist use matching theme keys.
- Slim Settings now includes a dedicated **Notifications** tab for Windows notification preferences (global enable, threshold, quiet hours, and event toggles).

## Files Modified

### Core Models
- `AIUsageTracker.Core/Models/AppPreferences.cs`
  - Added `AppTheme` enum
  - Added `Theme` property to `AppPreferences`

### UI Layer
- `AIUsageTracker.UI.Slim/MainWindow.xaml`
  - Added ThemeBtn to header
  - Added x:Name attributes to HeaderBorder, FooterBorder
  - Updated RefreshBtn ToolTip to include keyboard shortcut
  - Added KeyDown handler binding
  
- `AIUsageTracker.UI.Slim/MainWindow.xaml.cs`
  - Added `using System.Windows.Input;`
  - Added `OnKeyDown` event handler (line ~238)
  - Added `ThemeBtn_Click` handler
  - Added `ApplyTheme()` method
  - Updated `ApplyPreferences()` to call `ApplyTheme()`

- `AIUsageTracker.UI.Slim/App.xaml`
  - Global themed scrollbar style using dynamic resources
  - Improved button visuals for light themes (border + hover/pressed)

- `AIUsageTracker.UI.Slim/App.xaml.cs`
  - Central `ApplyTheme(AppTheme)` switch with full theme catalog
  - Non-blocking preference load at startup with safe fallback
  - Frozen-brush-safe resource updates

- `AIUsageTracker.UI.Slim/SettingsWindow.xaml.cs`
  - Friendly theme labels with enum-backed selection
  - Notification preference loading/saving via monitor preferences API

- `AIUsageTracker.Web/wwwroot/css/themes.css`
  - Matching theme tokens for the full catalog

- `AIUsageTracker.Web/wwwroot/js/theme.js`
  - Expanded supported theme key list

- `AIUsageTracker.Web/Pages/Shared/_Layout.cshtml`
  - Expanded theme dropdown options

## User Experience Improvements

### Power Users
- Keyboard shortcuts allow quick actions without mouse
- Professional workflow similar to other desktop apps
- Faster data refresh with Ctrl+R
- Quick theme toggle for different lighting conditions

### Accessibility
- Light theme for high visibility in bright environments
- Dark theme for reduced eye strain in low light
- Consistent color contrast maintained across themes

### Testing Instructions

**Keyboard Shortcuts:**
1. Open the application
2. Press Ctrl+R - should trigger refresh
3. Press Ctrl+P - should toggle privacy
4. Press Ctrl+T - should switch theme
5. Press F2 - should open settings
6. Press Escape - should close window

**Theme System:**
1. Open application (default: dark theme)
2. Click 🌙 button - should switch to light theme
3. Click ☀️ button - should switch back to dark theme
4. Close and reopen - theme should persist
5. Check preferences.json - theme setting should be saved

**Expected Behavior:**
- Theme toggles smoothly without flickering
- All UI elements (borders, buttons, text) update colors
- Keyboard shortcuts work in both themes
- Preferences are saved immediately on change
- Window maintains its size and position when theme changes

## Technical Notes

### Color System

**Dark Theme Colors:**
```csharp
WindowBg = Color.FromRgb(30, 30, 30);      // #1E1E1E
HeaderFooterBg = Color.FromRgb(37, 37, 38); // #252526
Foreground = Brushes.White;
ButtonBg = Color.FromRgb(68, 68, 68);     // #444444
```

**Light Theme Colors:**
```csharp
WindowBg = Color.FromRgb(243, 243, 243);    // #F3F3F3
HeaderFooterBg = Color.FromRgb(230, 230, 230); // #E6E6E6
Foreground = Brushes.Black;
ButtonBg = Color.FromRgb(187, 187, 187);  // #BBBBBB
```

### Code Quality
- No breaking changes to existing functionality
- Uses existing event handlers (PrivacyBtn_Click, SettingsBtn_Click, etc.)
- Follows existing code patterns and naming conventions
- Minimal code duplication

### Performance
- Theme application is instantaneous; preference loading is non-blocking at startup
- Keyboard shortcuts use existing synchronous handlers
- No performance impact on normal usage

## Future Enhancements

Potential improvements for theme system:
1. **Auto theme detection**: Follow system theme (Windows settings)
2. **Shared theme manifest**: Single source-of-truth tokens for Slim + Web
3. **High contrast mode**: Enhanced accessibility option
4. **Custom themes**: Allow user-defined accent colors
5. **Theme previews**: Small live swatches in theme selectors

Potential improvements for keyboard shortcuts:
1. **Customizable shortcuts**: Allow users to remap keys
2. **Shortcut reference**: Help dialog showing all shortcuts
3. **Global hotkeys**: Work even when window is not focused

