# System Theme (Auto-Detect OS Preference)

## Summary

Add a "System" theme option that automatically resolves to Dark or Light based on the current OS theme preference, with real-time reactivity when the OS theme changes.

## Design

### Core Model

- Add `System` to the `AppTheme` enum as a new value.
- `System` is a **virtual theme** — it has no color palette of its own. It resolves to `Dark` or `Light` at runtime based on the OS preference.
- Persisted as `AppTheme.System` in preferences. Never substituted with Dark/Light on disk — always resolved fresh at runtime.

### WPF Slim UI

**Detection:**
- Read Windows registry key: `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme`
  - Value `1` = Light theme
  - Value `0` = Dark theme
  - Missing key = fallback to Dark

**Real-time reactivity:**
- Subscribe to `Microsoft.Win32.SystemEvents.UserPreferenceChanged`
- On `UserPreferenceCategory.General`, re-read the registry and re-apply the resolved theme
- Unsubscribe on app shutdown to prevent leaks

**ApplyTheme modification:**
- `ApplyTheme(AppTheme.System)` resolves to Dark or Light, then applies that palette
- All existing theme logic (brush mutation, DynamicResource bindings) works unchanged

**Settings UI:**
- Add "System (Auto)" option at the top of the theme combo box in `GetThemeOptions()`
- Card preview shows the currently resolved palette

### Web UI

**Detection:**
- Use `window.matchMedia('(prefers-color-scheme: dark)')` to detect OS preference
- Returns true = dark, false = light

**Real-time reactivity:**
- Listen for `change` event on the `matchMedia` object
- Re-resolve and apply the dark/light CSS data-theme attribute

**Resolution:**
- "system" maps to existing "dark" or "light" `[data-theme]` CSS blocks
- No new CSS required — reuses existing Dark and Light theme definitions

**Settings UI:**
- Add "System" option to the `<select id="theme-select">` dropdown

### Files to Modify

| File | Change |
|------|--------|
| `AIUsageTracker.Core/Models/AppTheme.cs` | Add `System` enum value |
| `AIUsageTracker.UI.Slim/App.Themes.cs` | Add resolution logic in `ApplyTheme()` + OS listener setup/teardown |
| `AIUsageTracker.UI.Slim/App.xaml.cs` | Subscribe/unsubscribe OS theme change events |
| `AIUsageTracker.UI.Slim/SettingsWindow.xaml.cs` | Add "System (Auto)" to `GetThemeOptions()` |
| `AIUsageTracker.UI.Slim/App.Screenshots.cs` | Handle `AppTheme.System` in screenshot rendering |
| `AIUsageTracker.Web/wwwroot/js/theme.js` | Add "system" to theme list, add `matchMedia` listener |
| `AIUsageTracker.Web/Pages/Shared/_Layout.cshtml` | Add "System" option to dropdown |
| `AIUsageTracker.Web/wwwroot/css/themes.css` | Add `[data-theme="system"]` block that delegates to dark/light via CSS |

### Out of Scope

- No custom mapping (e.g., "system maps to Dracula instead of Dark"). System always resolves to Dark or Light.
- No intermediate themes (e.g., using a Catppuccin theme based on OS preference). System = Dark or Light only.
- No per-provider theming.
