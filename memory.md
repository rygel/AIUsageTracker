# AI Consumption Tracker - Memory State

## Current Status

- Branch: `feature/provider-refresh-backoff-breaker`
- PR: https://github.com/rygel/AIConsumptionTracker/pull/129
- Latest baseline commit: `504c90c` (plus uncommitted main screenshot scope update)

## Current State Variant

- Variant: **truthful-fixtures-with-reset-countdown**
- Fixture/data policy:
  - Use verified real source data (provider API snapshots / local agent data).
  - Do not invent models, account names, quotas, resets, or spend values.
  - If correct data is unavailable or uncertain, stop and ask.
- Screenshot fixture policy:
  - All **Plans & Quotas** fixture providers/submodels include `NextResetTime`.
  - **Pay As You Go** fixture rows show `Connected` (no synthetic spend values).
  - Main dashboard screenshot fixture excludes unsupported PAYG rows: `OpenAI`, `DeepSeek`, `Minimax (International)`, `OpenRouter`.
  - Main dashboard capture window is intentionally taller so all shown rows are visible.
- Reset label policy:
  - Relative reset UI must always show explicit time (`0m`, `Xm`, `Xh Ym`, `Xd Yh`), never `Ready`.

## Recent Commits (Newest First)

- `504c90c` ui: add quota reset countdowns in fixtures
- `9eeddb4` ui: show explicit reset countdown and trim README screenshots
- `01a7f55` docs: mandate correct source data usage
- `a883a2a` test: align antigravity fixtures with live snapshot models
- `bdc9f1b` docs: add fixture synchronization guidelines
- `cdfa248` docs: align screenshot fixtures with real antigravity responses
- `7acc316` docs: remove fabricated account labels from dashboard screenshot
- `6f6bc27` docs: refresh slim screenshots with richer synthetic data
- `c6a2fd8` docs: use relative scaling for README screenshots
- `1dc19f6` docs: scale embedded screenshots for readability
- `a7f9fa1` feat(ui-slim): expand deterministic settings screenshots
- `95893da` fix(agent): keep provider history retention indefinite

## What Was Updated in This Variant

### Antigravity Fixtures & Tests

- Replaced outdated snapshot labels (`claude-3.7-sonnet`, `mystery-model`) with current real model set.
- Synced tests and deterministic screenshot fixtures with the same real model names.
- Current Antigravity model set in fixtures:
  - `Claude Opus 4.6 (Thinking)`
  - `Claude Sonnet 4.6 (Thinking)`
  - `Gemini 3 Flash`
  - `Gemini 3.1 Pro (High)`
  - `Gemini 3.1 Pro (Low)`
  - `GPT-OSS 120B (Medium)`

### UI/README/Design Policy

- Slim + Web relative reset labels changed to explicit countdown (no `Ready`).
- README screenshot section reduced to:
  - Dashboard (main interface)
  - Providers
  - Link to User Manual for full screenshot gallery.
- Added `docs/test_fixture_sync.md` and AGENTS rule to keep fixtures synced with real data.
- DESIGN.md now explicitly requires correct source data and ask-first behavior when data is missing/uncertain.

## Key Files Touched Recently

- `AIConsumptionTracker.UI.Slim/MainWindow.xaml.cs`
- `AIConsumptionTracker.UI.Slim/SettingsWindow.xaml.cs`
- `AIConsumptionTracker.Web/Pages/Index.cshtml`
- `AIConsumptionTracker.Tests/TestData/Providers/antigravity_user_status.snapshot.json`
- `AIConsumptionTracker.Tests/Infrastructure/Providers/AntigravityProviderTests.cs`
- `docs/test_fixture_sync.md`
- `DESIGN.md`
- `README.md`
- `docs/screenshot_dashboard_privacy.png`
- `docs/screenshot_settings_privacy.png`
- `docs/screenshot_settings_providers_privacy.png`

## Known Workspace Note

- Untracked file `nul` exists at repo root. Avoid root-wide search commands that touch it.
