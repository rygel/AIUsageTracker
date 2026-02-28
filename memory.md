# AI Usage Tracker - Memory State

## Current Status

- Branch: `feature/slim-ui-theming`
- PR: https://github.com/rygel/AIConsumptionTracker/pull/138
- Working focus: Slim UI theming, OpenAI/Codex identity polish, installer Start Menu structure, and user manual screenshot coverage

## Current State Variant

- Variant: **post-release-2.2.3-slim-ui-theming-and-ux-polish**
- UI policy:
  - Provider cards are alphabetically ordered by display name in main groups and Settings.
  - Non-actionable operational detail rows (for example `Primary Window` / `Credits`) are hidden from sub-provider lists and sub-tray selectors.
  - GitHub username lookup in Slim Settings must not spawn GitHub CLI processes.
  - OpenAI/Codex identity display prefers email-like values when present, with non-email fallbacks.
  - Slim Settings now includes a persisted light/dark theme selector (`AppTheme`).
- Tray policy:
  - Tray icon creation must tolerate varying working directories by resolving `app_icon.ico` from multiple candidate paths and falling back gracefully.
- Screenshot policy:
  - Headless screenshot checks are authoritative in CI (`windows-2025` runner).
  - Deterministic screenshot mode uses a fixed clock for repeatable rendering.
  - If baseline drift is CI-only, use the uploaded CI artifact as baseline source of truth.

## Recent Commits (Newest First)

- `c23726e` feat(ui): add light/dark theme support in Slim settings
- `2861018` fix(ui): improve identity display and update docs
- `d00bfdf` chore(release): prepare v2.2.3 (#136)
- `6cd4c86` fix(ci): accept legacy UI exe in upgrade smoke test (#135)
- `88892bd` chore(release): prepare v2.2.2 (#134)

## What Was Updated in This Variant

### Slim UI Identity and Ordering

- Copilot/OpenAI account identity display avoids placeholders (`User`, `Unknown`) and now prefers email-style identity for OpenAI/Codex when available.
- Main window Plans & Quotas and Pay As You Go groups are sorted alphabetically by friendly provider name.
- Settings provider cards and detail rows are sorted alphabetically for stable scanning.

### Slim UI Theming

- Added `Theme` selection in Settings (Layout tab) backed by `AppPreferences.Theme`.
- Implemented runtime palette switching in `App.ApplyTheme(AppTheme)` for dark/light resource brushes.
- Theme preference is loaded at startup and applied globally.

### Installer and Documentation

- Installer Start Menu entries now place app shortcuts in an `Applications` submenu while keeping uninstall at group root.
- User manual now links/embeds auto-generated screenshots and documents Slim + Web pages.

### Slim UI Detail Presentation

- Operational details (`Primary Window`, `Credits`) are excluded from sub-provider render blocks.
- Sub-tray options now include only actionable percentage-based detail rows.

### Screenshot Baseline Stabilization

- Deterministic screenshot mode uses a fixed local timestamp to reduce run-to-run drift.
- CI screenshot artifacts were used to synchronize baselines where local and CI rendering differed.
- Remaining known sensitivity: providers tab screenshot may drift if deterministic fixture data changes.

## Key Files Touched Recently

- `AIUsageTracker.UI.Slim/MainWindow.xaml.cs`
- `AIUsageTracker.UI.Slim/SettingsWindow.xaml.cs`
- `AIUsageTracker.UI.Slim/App.xaml.cs`
- `AIUsageTracker.UI.Slim/SettingsWindow.xaml`
- `AIUsageTracker.Infrastructure/Providers/GitHubCopilotProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/OpenAIProvider.cs`
- `scripts/setup.iss`
- `docs/user_manual.md`

## Known Workspace Note

- Untracked file `nul` exists at repo root. Avoid broad root operations that may touch it.
- Current working tree includes an uncommitted tray icon resiliency update in `AIUsageTracker.UI.Slim/App.xaml.cs`.
