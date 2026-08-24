# User manual

Slim UI, Web UI, and CLI talk to the Monitor. Version: `Directory.Build.props` `<TrackerVersion>` (`2.4.7`). XAML title is “AI Usage Tracker”; runtime sets `AI Usage Tracker {displayVersion}` (`MainWindow.xaml.cs`).

Headless screenshots: `docs/screenshot_*_privacy.png` (`--test --screenshot`). Compared baselines (`ScreenshotBaselineTests`): `screenshot_dashboard_privacy.png`, `screenshot_settings_providers_privacy.png`, `screenshot_settings_layout_privacy.png`, `screenshot_settings_history_privacy.png`, `screenshot_info_privacy.png`.

## Slim dashboard

Header: title, version, Privacy (Ctrl+P), close. Close hides to tray (`CloseBtn_Click` → `Hide()`).

Footer (`MainWindow.xaml`): `StatusLed`, **Top**, **Show Used**, Refresh (Ctrl+R), Web UI, Monitor start/stop, Settings (F2). Escape / Ctrl+Q also hide.

Tray menu (`App.TrayIcon.cs`): Show, Info, Exit.

`ShowAll` and `StayOpen` exist on `AppPreferences` but have no Slim bindings.

## Settings

Tabs: Providers, Cards, Layout, Notifications, History, Monitor, Updates (`SettingsWindow.xaml`). Footer: **Scan for Keys**, **Refresh Data**.

**Providers** — keys, tray and notify checkboxes. Remove-key control is `StandardApiKey` + `HasStoredApiKey` only. GitHub Copilot (`ExternalAuthStatus`) shows “Not Authenticated” / “Authenticated” (`SettingsWindow.Providers.cs`). Grok reads `%USERPROFILE%\.grok\auth.json`.

**Cards** — progress fill, dual quota bars, pace-aware colours, yellow/red thresholds (defaults 60 / 80), show-used, usage-rate, reset text. Progress-bar rules: `DESIGN.md`.

**Layout** — Always On Top, aggressive/Win32 topmost, **Start with Windows** (`WindowsStartupService` HKCU Run value `"AI Usage Tracker"`), theme (`design/theme-catalog.json`), fonts.

**Notifications** — “Enable notifications” (default off), “Threshold %” (default 90), event types, quiet hours.

**History** — recent snapshots.

**Monitor** — Export CSV, Export JSON, Backup Database, diagnostics, logs.

**Updates** — channel (stable/beta).

## Web

`dotnet run --project AIUsageTracker.Web` → `http://localhost:5100`. Routes: `/`, `/providers`, `/provider/{id}`, `/history`, `/charts`, `/analytics`, `/reliability`, `/data/{tableName?}`.

## CLI

`docs/cli_documentation.md`.

## Keys

Scan reads env vars (`docs/environment_variables.md`), Kilo/Roo files, and session auth. Updating only the Settings field does not change an upstream env var or `auth.json`. `act set-key` / `act remove-key` change tracker config. Removal does not delete `provider_history`.
