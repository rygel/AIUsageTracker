# CLAUDE.md

Follow `AGENTS.md`. Extra Claude-specific notes:

```powershell
pwsh -File scripts/kill-all.ps1
dotnet test AIUsageTracker.Tests/AIUsageTracker.Tests.csproj
```

`kill-all.ps1` `$targets`: `AIUsageTracker`, `AIUsageTracker.Monitor`, `AIUsageTracker.UI`, `AIUsageTracker.UI.Slim`, `AIConsumptionTracker.Agent`, `dotnet`, `MSBuild`.

Analyzer severities live in `.editorconfig`. Do not lower them or add suppressions; fix the code. `CA1031` is `error` for product code and `none` in the three test projects.

Monitor polls `IProvider.GetUsageAsync` and serves grouped snapshots. Slim calls `MonitorService.GetGroupedUsageAsync`, expands via `GroupedUsageDisplayAdapter.Expand`, filters with `MainWindowRuntimeLogic.PrepareForMainWindow`, renders cards.

`ProviderSettingsMode`: `StandardApiKey`, `AutoDetectedStatus`, `ExternalAuthStatus`, `SessionAuthStatus`. Remove-key button is shown only for `StandardApiKey` with `HasStoredApiKey` (`SettingsWindow.Providers.cs`).

After config save/remove, call `MonitorService.InvalidateGroupedUsageCache()`.

Update `docs/INDEX.md` when adding or removing a doc. Do not add “last reviewed” headers.

Run tests and report. The user accepts the end result, not each step. Tags/releases still need an explicit user request (`AGENTS.md`).
