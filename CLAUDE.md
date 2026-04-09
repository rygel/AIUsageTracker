# CLAUDE.md

## Build & Test

Before building, kill any running app/monitor instances that lock DLLs:

```powershell
pwsh -File scripts/kill-all.ps1
```

Run tests (capped at 4 cores per global CLAUDE.md):

```bash
dotnet test AIUsageTracker.Tests/AIUsageTracker.Tests.csproj -T 4
```

## Analyzer Rules — Do Not Weaken

The following `.editorconfig` rules are enforced at `error` severity with zero violations.
**Never lower their severity, add suppressions, or work around them.**
Fix the underlying code instead.

| Rule | Severity | What it enforces |
|------|----------|-----------------|
| CA1031 | warning | No catching general `Exception` — use specific types |
| CA1062 | error | Validate public method parameters for null |
| CA1307 | error | Explicit `StringComparison` on all string operations |
| CA2016 | error | Forward `CancellationToken` to async methods |
| CA2254 | error | Use structured logging templates, not interpolation |

If a new violation appears, fix it before pushing. Do not:
- Change severity to `suggestion` or `none`
- Add `#pragma warning disable`
- Add `[SuppressMessage]` attributes
- Raise analyzer thresholds to make CI pass

## Architecture

- **AIUsageTracker.UI.Slim** — WPF main window + settings dialog (net8.0-windows)
- **AIUsageTracker.Monitor** — Background agent that polls providers and serves usage data over HTTP
- **AIUsageTracker.Core** — Shared models, interfaces, monitor client
- **AIUsageTracker.Infrastructure** — Provider implementations (Synthetic, Codex, OpenAI, etc.)

### Data flow: Monitor → Main Window

1. Monitor polls each configured provider via `IProvider.GetUsageAsync(config)`
2. Results are grouped into `AgentGroupedUsageSnapshot` and served via HTTP with ETag caching
3. Main window polls `MonitorService.GetGroupedUsageAsync()` every 2-60 seconds
4. `GroupedUsageDisplayAdapter.Expand()` flattens the snapshot into `List<ProviderUsage>`
5. `MainWindowRuntimeLogic.PrepareForMainWindow()` filters by visibility and state
6. `RenderProviders()` builds the card UI

### Settings dialog interaction

- Settings loads its own copy of `_configs` and `_usages` from the monitor
- Config changes are auto-saved with 600ms debounce via `PersistAllSettingsAsync`
- Settings always shows all default providers (ShowInSettings=true) as configuration slots
- On close, `DialogResult = true` triggers main window to call `InitializeAsync()` which re-fetches everything
- After config saves/removals, `MonitorService.InvalidateGroupedUsageCache()` is called to prevent stale ETag responses

### Provider settings modes

- **StandardApiKey** — User-editable API key field (Synthetic, Mistral, Kimi, etc.)
- **SessionAuthStatus** — Session-based auth with status display (Codex, OpenAI)
- **AutoDetectedStatus** — Auto-discovered, read-only (Antigravity, OpenCode Zen)
- **ExternalAuthStatus** — External auth flow (GitHub Copilot)

Only StandardApiKey providers can have their keys deleted by the user.
