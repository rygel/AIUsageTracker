# AI Usage Tracker — agent rules

.NET 10 (`global.json` SDK `10.0.300`). Finish the work and run tests; do not stop for per-step approval. Do not create tags or GitHub releases unless the user asks. Never hardcode machine-specific paths; use environment variables (`%LOCALAPPDATA%`, `%USERPROFILE%`, `$HOME`).

Layout, ports, schema: `docs/ARCHITECTURE.md`.

## Never

- Wipe user settings on a single bad property. `AppPreferences` enums use `JsonStringEnumConverter`; a corrupt property must skip, not reset the file (`AppPreferences.cs`).
- Drop columns in SQLite table-recreation migrations. `ConvertTimestampsToEpochIfNeeded` CREATE/INSERT lists must match `EnsureColumn` (`DatabaseMigrationService`). Adding a column requires an `Assert.Contains` in `DatabaseMigrationServiceTests.RunMigrations_LegacyDatabaseWithoutEvolveMetadata_AddsMissingProviderColumns`.
- Let the Monitor decide rendering. `ProviderDefinition` is the source of truth (`QuotaWindows`, `FamilyMode`, `PlanType`, `IsQuotaBased`, `IsCurrencyUsage`, `ShowInMainWindow`). No `.OfType` / hardcoded fallbacks in the card pipeline.
- Filter configured providers out of the UI because `IsAvailable=false`. Show cached data immediately.
- Null `RawJson` when privacy is on. `ProviderUsageProcessingPipeline.NormalizeUsage` clears `AccountName` and `ConfigKey`, then `ProviderRefreshService` persists that usage. `UsageDatabase.StoreRawSnapshotAsync` still writes `RawJson`.
- Delete `provider_history`. Filter placeholders before insert. `raw_snapshots` older than 7 days are deleted (`CleanupOldSnapshotsAsync` uses `604800` seconds).
- Push to `main` or `develop`. PRs: beta → `develop`, stable → `main`.
- Invent fixture payloads. Same-PR updates: tests + screenshot fixtures (`docs/test_fixture_sync.md`).

## Commands

```powershell
pwsh -File scripts/kill-all.ps1   # also stops every local `dotnet` and `MSBuild` process
dotnet build AIUsageTracker.sln --configuration Debug
dotnet test AIUsageTracker.Tests/AIUsageTracker.Tests.csproj --configuration Debug
dotnet run --project AIUsageTracker.Monitor
dotnet run --project AIUsageTracker.Web     # http://localhost:5100
dotnet run --project AIUsageTracker.UI.Slim
./scripts/pre-commit-check.sh
.\scripts\publish-app.ps1 -Runtime win-x64
.\scripts\sonar.ps1   # requires SONAR_TOKEN; optional SONAR_HOST_URL (default http://localhost:9000)
```

Headless screenshots: `dotnet run --project AIUsageTracker.UI.Slim --configuration Release -- --test --screenshot` then `pwsh -File scripts/verify-doc-images.ps1`.

Releases: `docs/release-process.md`. Version: `Directory.Build.props` `<TrackerVersion>`. Check tags before bumping: `git tag -l "v*" --sort=-v:refname`. Installer default dir: `{autopf}\AIUsageTracker` (`scripts/setup.iss`).

Async UI: `docs/wpf_async_best_practices.md`. Progress bars: `DESIGN.md`. Decisions: `docs/adr/`. Index: `docs/INDEX.md`.

Startup refresh (`ProviderRefreshService.ExecuteAsync`): empty history → `ScanForKeysAsync` then `forceAll: true`. Existing history → serve cache, then `GetStartupRefreshProviderIds()` (only Antigravity sets `RefreshOnStartupWithCachedData`). Windows power resume queues `forceAll: true` (`Monitor/Program.cs` `PowerStateListener` `onResume`).

## Code

File-scoped namespaces, Allman braces, 4-space indent, `_camelCase` fields, `Async` suffix, `ILogger<T>`, `System.Text.Json`. Interfaces only when tests mock them. Tests: xUnit + Moq, `Method_Scenario_Expected`. Catch blocks log or rethrow.

Slim shortcuts (`MainWindow.xaml.cs`): Ctrl+R refresh, Ctrl+P privacy, Ctrl+Q / Escape hide (not exit), F2 settings. No Ctrl+T.

Themes: `design/theme-catalog.json` (CI parity). Web HTMX: `https://unpkg.com/htmx.org@1.9.12` (`Pages/Shared/_Layout.cshtml`).

Slim screenshot baselines: CI on `windows-2025` is authoritative (`slim-screenshot-baseline.yml`). Sync drifted `docs/screenshot_*_privacy.png` from the `slim-ui-screenshots` artifact; do not add files unless the workflow and verifier change together.
