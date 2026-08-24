# ADR-006: kill-all process name

Accepted 2026-04-05.

Slim’s process name is `AIUsageTracker` (`AssemblyName` in `AIUsageTracker.UI.Slim.csproj`), not `AIUsageTracker.UI.Slim`. `scripts/kill-all.ps1` `$targets` includes `AIUsageTracker`. The script also stops `dotnet` and `MSBuild`.
