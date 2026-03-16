# Analyzer Warning Remediation Plan

## Summary

This document is the current handoff plan for repository-wide warning reduction. The previous plan was outdated and no longer matched the actual warning distribution. A fresh Debug solution build shows that the remaining backlog is now concentrated in a few repeatable syntax/style families plus a smaller set of deeper async and method-structure warnings.

The goal is to reduce warnings quickly without mixing low-risk mechanical cleanup with behavior-sensitive refactors.

## Current Baseline

- Fresh local command used:
  - `dotnet build AIUsageTracker.sln --configuration Debug -m:1`
- Current warning count from that build:
  - approximately `760`

### Biggest Warning Families

- `MA0051` - 136
- `SA1201` - 92
- `IDE0065` - 84
- `SA1202` - 72
- `SA1204` - 68
- `SA1117` - 32
- `IDE0161` - 28
- `VSTHRD200` - 26
- `MA0016` - 22
- `VSTHRD001` - 20
- `SA0001` - 20

## Hotspots

### Highest-Warning Projects

- `AIUsageTracker.Infrastructure` - about 190
- `AIUsageTracker.Tests` - about 160
- `AIUsageTracker.Monitor` - about 106
- `AIUsageTracker.Core` - about 90
- `AIUsageTracker.UI.Slim` - about 80
- `AIUsageTracker.CLI` - about 66
- `scripts/Seeder` - about 28

### Highest-Warning Files Seen in the Fresh Build

- `AIUsageTracker.CLI\Program.cs`
- `AIUsageTracker.UI.Slim\MainWindow.xaml.cs`
- `scripts\Seeder\Program.cs`
- `AIUsageTracker.UI.Slim\SettingsWindow.xaml.cs`
- `AIUsageTracker.Core\MonitorClient\MonitorService.cs`
- `AIUsageTracker.Infrastructure\Providers\ZaiProvider.cs`
- `AIUsageTracker.Infrastructure\Providers\AntigravityProvider.cs`
- `AIUsageTracker.Monitor\Services\ProviderRefreshService.cs`
- `AIUsageTracker.Monitor\Services\UsageDatabase.cs`
- `AIUsageTracker.Infrastructure\Providers\CodexProvider.cs`

## What Recently Improved

- Recent cleanup work appears to have already removed a meaningful amount of changed-file analyzer noise, especially in the areas touched by the latest PRs.
- The remaining backlog is now dominated less by random drift and more by a small set of repeatable warning families:
  - namespace and using placement
  - member ordering
  - blank-line and argument formatting
  - long-method warnings
  - async analyzer warnings

## Concrete Execution Order

### 1. Mechanical Syntax and Style Batch

Target these first:

- `IDE0065` - move `using` directives outside namespace declarations
- `IDE0161` - convert remaining block namespaces to file-scoped namespaces where it matches repo conventions
- `SA1201`, `SA1202`, `SA1204`, `SA1210` - reorder members and usings
- `SA1516`, `SA1117`, `SA1118`, `SA1122`, `SA1413` - spacing, argument, and formatting fixes

Why this batch goes first:

- high volume
- low behavioral risk
- mostly mechanical edits
- fast warning-count reduction
- removes noise before deeper refactors

Best initial targets:

- `AIUsageTracker.CLI\Program.cs`
- `AIUsageTracker.CLI\AppJsonContext.cs`
- `scripts\Seeder\Program.cs`
- `AIUsageTracker.Tests\**` files with `IDE0065` or `IDE0161`
- `AIUsageTracker.Infrastructure\**` files with `IDE0065` or `IDE0161`

### 2. Policy Decision Batch

Resolve `SA0001`.

Current meaning:

- StyleCop XML comment analysis is disabled by project configuration
- the compiler emits `SA0001` once per project

Recommended decision:

- either explicitly suppress `SA0001` repo-wide if this configuration is intentional
- or re-enable the expected XML documentation analysis policy consistently

Do not leave this undecided, because it adds stable noise without actionable value.

### 3. Async Analyzer Batch

After the mechanical cleanup, address:

- `MA0004`
- `VSTHRD001`
- `VSTHRD200`

Focus on library and runtime code first:

- `AIUsageTracker.Core`
- `AIUsageTracker.Infrastructure`
- `AIUsageTracker.Monitor`

Guidance:

- add `ConfigureAwait(false)` only where library code truly should not capture context
- be more careful in WPF UI code and test code
- validate each batch because these are more behavior-sensitive than formatting fixes

### 4. Method-Structure Batch

Tackle `MA0051` last.

These warnings usually require helper extraction or logic reshaping, not just reformatting.

Highest-value starting points:

- `AIUsageTracker.CLI\Program.cs`
- `AIUsageTracker.Core\MonitorClient\MonitorService.cs`
- `AIUsageTracker.Core\MonitorClient\MonitorLauncher.cs`
- provider files with long parsing and mapping methods such as:
  - `OpenAIProvider`
  - `MinimaxProvider`
  - `DeepSeekProvider`

### 5. Collection-Abstraction and Smaller Semantic Cleanup

Address `MA0016` and similar semantic warnings after the large style passes.

These are often easy individually, but they are more scattered and some touch public signatures, so they are better handled once the warning list is smaller and easier to reason about.

## Suggested Workstreams

### Workstream A: CLI and Seeder Mechanical Cleanup

Goal:

- eliminate `IDE0065`, `IDE0161`, and easy ordering warnings in `AIUsageTracker.CLI` and `scripts\Seeder`

Why:

- high warning density
- low behavioral risk

### Workstream B: Tests Mechanical Cleanup

Goal:

- sweep `AIUsageTracker.Tests` for namespace, using, ordering, and other mechanical style warnings

Why:

- many warnings
- low runtime risk
- good bulk reduction

### Workstream C: Infrastructure Mechanical Cleanup

Goal:

- remove syntax and style warnings in `AIUsageTracker.Infrastructure`

Why:

- largest project-level backlog

### Workstream D: Monitor and Core Async Cleanup

Goal:

- handle `MA0004` and `VSTHRD*` carefully in non-UI runtime code

Why:

- fewer warnings than style cleanup, but more correctness-sensitive

### Workstream E: Long-Method Refactors

Goal:

- reduce `MA0051`

Why:

- harder work
- should not be mixed into purely mechanical cleanup batches

## Validation Strategy

After each workstream:

- `dotnet build AIUsageTracker.sln --configuration Debug -m:1`

For changed files:

- `./scripts/pre-push-validation.ps1`

For regression tracking:

- use the existing analyzer gate workflow and artifacts
- note that the current `.analyzer-gate/scope.json` only targets `AIUsageTracker.Web` and `AIUsageTracker.Web.Tests`

## Recommended Follow-up to the Analyzer Gate

- Expand `.analyzer-gate/scope.json` once repo-wide warning cleanup becomes active.
- Right now it is not a good source of truth for overall warning reduction because it only scopes `Web` and `Web.Tests`.
- If the team wants to measure progress across the repo, add tracked path prefixes and build targets for the projects being cleaned in each wave.

## Important Notes for the Next Engineer

- Keep mechanical style cleanup separate from behavior changes.
- Avoid mixing provider logic refactors with warning cleanup unless a warning fix truly requires it.
- Prefer project-by-project or workstream-by-workstream commits so warning reductions are easy to review.
- Re-run builds after every batch, because member reordering and namespace conversion can create merge or generated-code edge cases.
- `SA0001` should be resolved as a policy choice early, otherwise the warning count will keep looking artificially inflated.
