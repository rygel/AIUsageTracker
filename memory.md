# AI Usage Tracker - Memory State

## Current Status

- Branch: `release/v2.2.0` (content targets release `2.2.1`)
- PR: https://github.com/rygel/AIConsumptionTracker/pull/132
- Working focus: Slim UI identity/ordering fixes, screenshot baseline stabilization, and release prep

## Current State Variant

- Variant: **release-2.2.1-ui-identity-ordering-and-baseline-sync**
- UI policy:
  - Provider cards are alphabetically ordered by display name in main groups and Settings.
  - Non-actionable operational detail rows (for example `Primary Window` / `Credits`) are hidden from sub-provider lists and sub-tray selectors.
  - GitHub username lookup in Slim Settings must not spawn GitHub CLI processes.
- Screenshot policy:
  - Headless screenshot checks are authoritative in CI (`windows-2025` runner).
  - Deterministic screenshot mode uses a fixed clock for repeatable rendering.
  - If baseline drift is CI-only, use the uploaded CI artifact as baseline source of truth.

## Recent Commits (Newest First)

- `3b941c0` test(ui): sync providers screenshot baseline
- `73db1a1` test(ui): freeze deterministic screenshot clock
- `9a9fccd` test(ui): align baselines with CI renderer
- `4da8921` fix(ui): disable gh username lookup and refresh baselines
- `a9c2745` fix(ui): improve codex/copilot identity and ordering

## What Was Updated in This Variant

### Slim UI Identity and Ordering

- Copilot/OpenAI account identity display now accepts non-email identities and avoids placeholders (`User`, `Unknown`).
- Main window Plans & Quotas and Pay As You Go groups are sorted alphabetically by friendly provider name.
- Settings provider cards and detail rows are sorted alphabetically for stable scanning.

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
- `AIUsageTracker.Infrastructure/Providers/GitHubCopilotProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/OpenAIProvider.cs`
- `scripts/verify_screenshot_baseline.ps1`
- `docs/screenshot_settings_providers_privacy.png`

## Known Workspace Note

- Untracked file `nul` exists at repo root. Avoid broad root operations that may touch it.
