# AI Consumption Tracker - Memory State

## Current Status

- Branch: `feature/headless-screenshot-tabs`
- PR: https://github.com/rygel/AIConsumptionTracker/pull/126
- Base: `main` @ `v2.1.2` (`d93bc31`)
- Recent commits:
  - `cdd364f` ui: sharpen headless marketing screenshots
  - `c5e1a33` ci: add screenshot baseline diff gate
  - `39c37da` ci: add provider contract drift workflow
  - `e8ae7ef` docs: consolidate documentation directory
  - `ea8dc7f` ci: add slim screenshot capture lane
  - `499bcb1` feat: improve slim screenshot workflow

## What Was Recently Changed

### API Contract Drift + Upgrade + Telemetry Hardening

- Added `scripts/verify-agent-openapi-contract.ps1`:
  - starts Agent from build output,
  - reads live endpoint/method inventory from `/api/diagnostics`,
  - compares against `AIConsumptionTracker.Agent/openapi.yaml`,
  - fails on drift in either direction.
- `test.yml` now runs `Verify Agent OpenAPI contract drift` after build.
- Added `scripts/smoke-test-upgrade.ps1` and extended publish `smoke-test-windows` job to:
  - download previous win-x64 installer asset from GitHub releases,
  - run old->new silent upgrade,
  - verify key settings files remain unchanged,
  - run post-upgrade Agent health check.
- Added refresh telemetry in Agent (`ProviderRefreshService`) and surfaced it in `/api/diagnostics` (`refresh_telemetry`).
- Added lightweight Slim client telemetry in `AgentService` for usage/refresh latency + error rates, surfaced in Settings > Agent diagnostics log.
- Updated OpenAPI schema (`AIConsumptionTracker.Agent/openapi.yaml`) for diagnostics endpoint additions.

### README/Docs Image Integrity Check

- Added `scripts/verify-doc-images.ps1` to detect broken local image references in `README.md` and all markdown files under `docs/`.
- Validates both markdown `![...](...)` and HTML `<img src="...">` patterns.
- CI `test.yml` now runs this check early via `Verify README/docs image references`.

### Release Artifact Hardening + SBOM

- `Publish & Distribute` now has two additional gates:
  - `release-metadata`: generates `SHA256SUMS.txt` and CycloneDX `sbom.cdx.json` from built release artifacts.
  - `smoke-test-windows`: installs x64 setup artifact and verifies Agent `/api/health` before release creation.
- Release packaging now verifies checksum/SBOM outputs and publishes both metadata files as release assets.
- Added `scripts/smoke-test-release.ps1` for reusable installer + health smoke validation.
- Updated `docs/release_process.md` with new release metadata/smoke steps and troubleshooting notes.

### Slim Screenshot Pipeline (Headless)

- `--test --screenshot` now runs fully in-app/headless (no desktop capture).
- Rendering uses deterministic sample data for repeatable baselines.
- Screenshots are exported as opaque PNG (`Format24bppRgb`), now at 2x scale for better marketing sharpness.
- Settings screenshots now include every tab:
  - `screenshot_settings_providers_privacy.png`
  - `screenshot_settings_layout_privacy.png`
  - `screenshot_settings_history_privacy.png`
  - `screenshot_settings_agent_privacy.png`
  - plus compatibility `screenshot_settings_privacy.png`

### Screenshot Quality / Reliability Hardening

- Added `scripts/verify_screenshot_baseline.ps1`:
  - validates required screenshot files exist
  - fails when generated screenshots differ from committed baselines
- `scripts/capture_screenshot.ps1` is now the current screenshot entrypoint (delegates to `generate_screenshots.ps1`).
- CI `test.yml` now:
  - builds solution
  - generates headless Slim screenshots
  - verifies screenshot baselines
  - uploads screenshot artifacts (`if: always()`)

### Settings / Info UX Cleanup

- Diagnostics log removed from `InfoDialog` (Info is now focused on app/system info).
- Diagnostics log is now wired in `Settings > Agent` and refreshes after health/restart actions.
- Agent port display in Settings fixed via named `AgentPortText`.

### Provider Contract Drift Checks

- Added workflow `.github/workflows/provider-contract-drift.yml`:
  - runs weekly (schedule), on `workflow_dispatch`, and for relevant PR path changes
  - runs provider tests filtered by `Infrastructure.Providers`
  - uploads TRX results as artifacts

### Docs Consolidation

- Removed duplicate `doc/database.md`.
- `docs/` is now the canonical documentation directory.

## Key Screenshot Commands

```powershell
# Generate all privacy screenshots (headless)
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\capture_screenshot.ps1 -Configuration Debug

# Verify generated screenshots match committed baselines
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify_screenshot_baseline.ps1
```

## Important Files Touched in This Branch

- `AIConsumptionTracker.UI.Slim/App.xaml.cs`
- `AIConsumptionTracker.UI.Slim/MainWindow.xaml.cs`
- `AIConsumptionTracker.UI.Slim/SettingsWindow.xaml`
- `AIConsumptionTracker.UI.Slim/SettingsWindow.xaml.cs`
- `AIConsumptionTracker.UI.Slim/InfoDialog.xaml`
- `AIConsumptionTracker.UI.Slim/InfoDialog.xaml.cs`
- `scripts/capture_screenshot.ps1`
- `scripts/generate_screenshots.ps1`
- `scripts/verify_screenshot_baseline.ps1`
- `.github/workflows/test.yml`
- `.github/workflows/provider-contract-drift.yml`
- `.github/workflows/publish.yml`
- `AIConsumptionTracker.Agent/Program.cs`
- `AIConsumptionTracker.Agent/Services/ProviderRefreshService.cs`
- `AIConsumptionTracker.Agent/openapi.yaml`
- `AIConsumptionTracker.Core/AgentClient/AgentService.cs`
- `AIConsumptionTracker.Agent.Tests/ProviderRefreshServiceTests.cs`
- `AIConsumptionTracker.Tests/Core/AgentServiceTests.cs`
- `scripts/verify-agent-openapi-contract.ps1`
- `scripts/smoke-test-upgrade.ps1`
- `README.md`
- `scripts/smoke-test-release.ps1`
- `docs/release_process.md`
- `docs/screenshot_*_privacy.png`

## Known Workspace Note

- Untracked file `nul` exists at repo root (Windows special-name artifact). Avoid root-wide rg calls that hit it.

## Next Likely Work

- Optional release-signing follow-up (if certificate/signing policy is available).
