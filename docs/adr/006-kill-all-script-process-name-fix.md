# ADR-006: kill-all.ps1 Process Name Fix

## Status
Accepted — 2026-04-05

## Context

The `scripts/kill-all.ps1` script is used to kill running AIUsageTracker processes before building, since the app and monitor lock DLLs and prevent `dotnet build` from copying output assemblies.

The script's target list included `AIUsageTracker.UI.Slim` (the project name) but not `AIUsageTracker` (the actual process name after compilation). The compiled executable is `AIUsageTracker.exe` because the assembly name differs from the project name. This meant the script failed to kill the running app, leaving DLLs locked.

## Decision

Added `"AIUsageTracker"` to the `$targets` array in `kill-all.ps1` so the script matches the actual process name of the compiled executable.

## Consequences

- `scripts/kill-all.ps1` now reliably kills all AIUsageTracker processes before builds.
- No change to the kill logic — only the target list was extended.
- Documented in `CLAUDE.md` as the standard pre-build step.
