# Test Fixture Data Synchronization

This document defines how provider test fixtures and deterministic screenshot fixtures must stay aligned with real provider responses.

## Scope

- Unit test response fixtures in `AIUsageTracker.Tests\TestData\Providers\`.
- Inline provider response payloads in `AIUsageTracker.Tests\Infrastructure\Providers\*ProviderTests.cs`.
- Deterministic screenshot fixture data in:
  - `AIUsageTracker.UI.Slim\MainWindow.xaml.cs`
  - `AIUsageTracker.UI.Slim\SettingsWindow.xaml.cs`

## Source of Truth

- Real captured provider payloads (sanitized only for secrets/identifiers).
- Checked-in snapshots:
  - `AIUsageTracker.Tests\TestData\Providers\antigravity_user_status.snapshot.json`
  - `AIUsageTracker.Tests\TestData\Providers\codex_wham_usage.snapshot.json`

## Required Rules

1. Do not invent provider models, account identifiers, plans, quotas, or currencies.
2. If real payloads do not provide a field, leave it empty or omit derived details.
3. Keep naming and units exactly aligned with real payload semantics (remaining vs used, tokens vs credits, etc.).
4. When provider payload shape changes, update tests first, then update deterministic screenshot fixtures in the same PR.
5. If a fixture value is anonymized, preserve structure and meaning (only redact sensitive values).

## Current Antigravity Fixture Mapping

- Screenshot fixtures (`MainWindow` + `SettingsWindow`) use:
  - `Claude Opus 4.6 (Thinking)`
  - `Claude Sonnet 4.6 (Thinking)`
  - `Gemini 3 Flash`
  - `Gemini 3.1 Pro (High)`
  - `Gemini 3.1 Pro (Low)`
  - `GPT-OSS 120B (Medium)`
- Snapshot parser fixture (`antigravity_user_status.snapshot.json`) currently contains:
  - `Claude Opus 4.6 (Thinking)`
  - `Claude Sonnet 4.6 (Thinking)`
  - `Gemini 3 Flash`
  - `Gemini 3.1 Pro (High)`
  - `Gemini 3.1 Pro (Low)`
  - `GPT-OSS 120B (Medium)`

## Update Workflow

1. Capture and sanitize the new real provider response.
2. Update/add snapshot fixture under `AIUsageTracker.Tests\TestData\Providers\` (or inline realistic payload in provider tests when necessary).
3. Update assertions in provider tests.
4. Mirror the same reality in deterministic screenshot fixtures (`MainWindow` / `SettingsWindow`), without inventing extra model/account fields.
5. Regenerate screenshots:
   - `dotnet run --project .\AIUsageTracker.UI.Slim\AIUsageTracker.UI.Slim.csproj --configuration Release -- --test --screenshot`
6. Validate documentation image references:
   - `powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-doc-images.ps1`

