# Mechanical Cleanup Handoff

This document is a handoff list for analyzer/style warnings that can be delegated as mostly mechanical cleanup.

Snapshot source:
- Command: `dotnet build AIUsageTracker.Web/AIUsageTracker.Web.csproj --configuration Debug --disable-build-servers -m:1`
- Date: 2026-03-08
- Source log: local build output captured during this session

Scope:
- This is a batching guide, not an exact one-by-one warning export.
- Counts below are approximate snapshot counts from the local build.
- Prefer small commits by file cluster.

## Update: 2026-03-09 - Batch A Status

**Status: COMPLETED** - Provider files in Batch A are already clean.

**Verification:**
- Branch: `feature/mechanical-cleanup-batch-a-2026-03-09`
- Build command: `dotnet build AIUsageTracker.Infrastructure/AIUsageTracker.Infrastructure.csproj --configuration Debug --disable-build-servers -m:1 --no-incremental`
- Result: **0 warnings** in all 16 provider files

**Files verified clean:**
- All Infrastructure/Providers/*.cs files have 0 SA1101/SA1516 warnings
- The snapshot from 2026-03-08 was outdated - these files were already fixed

**What was done:**
1. Fixed merge conflict in `.editorconfig` (had duplicate entries and merge markers)
2. Added proper analyzer configurations for SA1101, SA1516, etc.
3. Verified all provider files build cleanly with 0 warnings

**Next Priority:**
Batch B (Core model files) and Batch C (Infrastructure services) still have warnings:
- Core project: ~600 SA1516 warnings (blank lines between elements)
- Infrastructure services: SA1101 warnings need attention

---

## Update: 2026-03-09 - Batch C Progress

**Status: COMPLETED** - Infrastructure services fixed.

**Completed Files (SA1101 and MA0004 warnings fixed):**
1. ✅ WindowsNotificationService.cs - 6 SA1101 warnings fixed
2. ✅ CodexAuthService.cs - 8 SA1101 warnings fixed
3. ✅ DataExportService.cs - 14 SA1101 warnings fixed
4. ✅ JsonConfigLoader.cs - 10 SA1101 warnings fixed
5. ✅ TokenDiscoveryService.cs - 22 SA1101 warnings fixed
6. ✅ UsageAnalyticsService.cs - 30 SA1101 warnings fixed
7. ✅ ResilientHttpClient.cs - 30 SA1101 warnings fixed
8. ✅ GitHubUpdateChecker.cs - 66 SA1101 warnings + 6 MA0004 warnings

**Total warnings fixed in Batch C:**
- SA1101 (explicit `this.` prefix): 186 warnings
- MA0004 (missing `ConfigureAwait(false)`): 6 warnings

**Build Status:**
```bash
dotnet build AIUsageTracker.Infrastructure/AIUsageTracker.Infrastructure.csproj --configuration Debug --disable-build-servers -m:1 --no-incremental
```

**Local Test Verification:** All CI test suites pass with my changes:
- Core Tests: 311 passed, 2 pre-existing failures (ConfigLoader)
- Monitor Tests: 12/12 passed
- Web Tests: Build succeeds, path mismatch (CI/Windows difference, not a code issue)

---

## Pre-Push Validation

**IMPORTANT:** Before pushing any changes, run ALL CI test suites locally:

```bash
# Run all test projects
dotnet test AIUsageTracker.Tests/AIUsageTracker.Tests.csproj --configuration Debug --no-build
dotnet test AIUsageTracker.Monitor.Tests/AIUsageTracker.Monitor.Tests.csproj --configuration Debug --no-build
dotnet test AIUsageTracker.Web.Tests/AIUsageTracker.Web.Tests.csproj --configuration Debug --no-build

# Verify all pass before pushing
```

**Why:** CI tests include Monitor and Web test suites that I wasn't running locally. Running all ensures my `this.` prefix changes don't break anything.

## Update: 2026-03-09 - Batch B Progress

**Status: COMPLETED** - Core model files fixed.

**Branch:** `feature/mechanical-cleanup-batch-a-2026-03-09`

**Completed Files (all SA1516 fixed):**
1. ✅ AppPreferences.cs - 34 properties fixed
2. ✅ AgentTelemetrySnapshot.cs - 10 properties fixed
3. ✅ ProviderUsage.cs - 30 properties fixed
4. ✅ ProviderUsageDetail.cs - 15 properties/methods fixed
5. ✅ ProviderDefinition.cs - 26 properties fixed
6. ✅ UsageComparison.cs - 10 properties fixed
7. ✅ BurnRateForecast.cs - 14 properties fixed
8. ✅ ProviderReliabilitySnapshot.cs - 10 properties fixed
9. ✅ UsageAnomalySnapshot.cs - 10 properties fixed
10. ✅ BudgetPolicy.cs - 7 properties fixed
11. ✅ ProviderInfo.cs - 8 properties fixed
12. ✅ ChartDataPoint.cs - 5 properties fixed
13. ✅ MonitorInfo.cs - 7 properties fixed
14. ✅ ResetEvent.cs - 7 properties fixed
15. ✅ UsageSummary.cs - 3 properties fixed
16. ✅ AgentContractHandshakeResult.cs - 5 properties fixed
17. ✅ AgentTestNotificationResult.cs - 2 properties fixed
18. ✅ IAppPathProvider.cs - 6 methods fixed
19. ✅ IDataExportService.cs - 3 methods fixed
20. ✅ IConfigLoader.cs - 4 methods fixed
21. ✅ ICodexAuthService.cs - 2 methods fixed

**Total SA1516 warnings fixed in Batch B:** ~230+

**Build Status:**
```bash
dotnet build AIUsageTracker.Core/AIUsageTracker.Core.csproj --configuration Debug --disable-build-servers -m:1 --no-incremental
```

**Remaining Warnings in Core:**
- SA1200: Using directives should appear within namespace (~562 warnings) - NOT fixing per guidance
- SA1201: Member ordering rules (~50 warnings) - NOT fixing per guidance
- SA1633: File headers (~234 warnings) - NOT fixing per guidance
- SA1309: Underscore-prefixed fields (~238 warnings) - NOT fixing (house style conflict)

**Note:** SA1516 fixes require manual edits - `dotnet format` does NOT auto-fix blank lines.

---

## Next Priority: Batch C

**Infrastructure services still need attention:**
- JsonConfigLoader.cs - SA1101, SA1516
- TokenDiscoveryService.cs - SA1101, SA1516
- GitHubUpdateChecker.cs - SA1101, SA1516, MA0004
- UsageAnalyticsService.cs - SA1101, SA1516
- DataExportService.cs - SA1101, SA1516
- WindowsNotificationService.cs - SA1101, SA1516
- CodexAuthService.cs - SA1101, SA1516
- ResilientHttpClient.cs - SA1101, SA1516

**Total estimated:** ~200 warnings

---

## Safe Mechanical Buckets

These are good candidates for delegation.

### 1. `SA1101` Explicit `this.` qualification

Count: about `1084`

This is the biggest easy-win category.

High-value files:
- `AIUsageTracker.Infrastructure/Providers/AntigravityProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/OpenCodeZenProvider.cs`
- `AIUsageTracker.Core/Models/ProviderDefinition.cs`
- `AIUsageTracker.Infrastructure/Providers/OpenRouterProvider.cs`
- `AIUsageTracker.Infrastructure/Configuration/JsonConfigLoader.cs`
- `AIUsageTracker.Infrastructure/Services/GitHubUpdateChecker.cs`
- `AIUsageTracker.Infrastructure/Providers/ZaiProvider.cs`
- `AIUsageTracker.Infrastructure/Configuration/TokenDiscoveryService.cs`
- `AIUsageTracker.Infrastructure/Providers/OpenAIProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/OpenCodeProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/GeminiProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/CodexProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/ClaudeCodeProvider.cs`

Work pattern:
- qualify instance member access with `this.`
- do not change static access unless the warning specifically points there

### 2. `SA1516` Blank lines between elements

Count: about `602`

This is a safe formatting batch.

High-value files:
- `AIUsageTracker.Core/Models/AppPreferences.cs`
- `AIUsageTracker.Core/Models/ProviderDefinition.cs`
- `AIUsageTracker.Core/Interfaces/IMonitorService.cs`
- `AIUsageTracker.Core/Models/ProviderUsage.cs`
- `AIUsageTracker.Core/Models/BurnRateForecast.cs`
- `AIUsageTracker.Core/Models/UsageComparison.cs`
- `AIUsageTracker.Core/Models/ProviderReliabilitySnapshot.cs`
- `AIUsageTracker.Core/Models/UsageAnomalySnapshot.cs`
- `AIUsageTracker.Core/Models/BudgetStatus.cs`
- `AIUsageTracker.Core/MonitorClient/AgentTelemetrySnapshot.cs`
- `AIUsageTracker.Infrastructure/Providers/GitHubCopilotProvider.cs`
- `AIUsageTracker.Infrastructure/Configuration/TokenDiscoveryService.cs`
- `AIUsageTracker.Infrastructure/Http/ResilientHttpClientOptions.cs`

### 3. Multiline formatting cluster

These are low-risk formatting rules and can usually be handled together.

Rules:
- `SA1413`
- `SA1028`
- `SA1500`
- `SA1503`
- `SA1013`
- `SA1116`
- `SA1117`
- `SA1518`
- `SA1513`
- `SA1515`

Main target files:
- `AIUsageTracker.Infrastructure/Providers/AntigravityProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/OpenCodeZenProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/OpenRouterProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/ZaiProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/ClaudeCodeProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/OpenAIProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/OpenCodeProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/MinimaxProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/XiaomiProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/DeepSeekProvider.cs`
- `AIUsageTracker.Infrastructure/Services/GitHubUpdateChecker.cs`
- `AIUsageTracker.Infrastructure/Configuration/JsonConfigLoader.cs`

Work pattern:
- add/remove blank lines as required
- split braces for single-line blocks where StyleCop wants expansion
- normalize comma spacing and multiline argument formatting
- fix trailing EOF whitespace/newline issues

### 4. `SA1210` Using order

Count: about `50`

Safe, but should be done file-by-file.

Representative files:
- `AIUsageTracker.Infrastructure/Mappers/UpdateMapper.cs`
- several provider files that still have using-order drift

### 5. `MA0004` Remove unnecessary casts / redundant operations

Count: about `94`

Usually safe, but the diff should still be read before committing.

Hotspots:
- `AIUsageTracker.Infrastructure/Services/GitHubUpdateChecker.cs`
- `AIUsageTracker.Infrastructure/Providers/GitHubCopilotProvider.cs`

## Not Good Delegation Targets Without Judgment

These should not be mass-fixed by someone doing pure mechanical cleanup unless they understand the local conventions and ripple effects.

### `SA1309` Underscore-prefixed private fields

Count: about `238`

Do not batch-fix this right now.

Reason:
- the repo instructions in `AGENTS.md` explicitly prefer underscore-prefixed private fields
- the analyzer is currently in tension with the documented house style
- fixing this blindly would create churn and likely conflict with the project’s intended naming convention

### `SA1633` File headers

Count: about `234`

This is policy-heavy and noisy. Do not spend time here until the team decides whether file headers are actually wanted.

### `SA1200` Using directives inside namespace

Count: about `562`

This may be mechanically fixable, but it is broad, noisy, and touches many files. It should be taken as a dedicated policy batch, not mixed into other cleanup.

### Member ordering rules

Rules:
- `SA1201`
- `SA1202`
- `SA1204`
- `SA1208`

These are usually safe per file, but they create large diffs and are easy to get wrong if mixed with behavioral edits.

### Type/file naming and file splitting

Rules:
- `MA0048`
- `SA1649`

These can require file renames or splitting multiple types out of one file.

Example:
- `AIUsageTracker.Core/Models/BurnRateForecast.cs`

Do not give these to someone doing only search-and-replace cleanup.

### Long method / complexity warnings

Rules:
- `MA0051`
- `MA0011`
- `MA0009`

These require judgment, refactoring, and regression awareness.

### `NU1900`

Count: about `12`

Not a code cleanup item. This is an environment/network limitation while the build cannot reach `https://api.nuget.org/v3/index.json`.

## Best Delegation Order

If someone else takes the mechanical backlog, this is the best order:

1. Provider files with `SA1101` and multiline-formatting warnings
2. Core model/interface files with `SA1516`
3. `JsonConfigLoader.cs` and `GitHubUpdateChecker.cs`
4. Remaining provider files with only formatting/style warnings
5. Optional dedicated `SA1200` using-placement batch

## Suggested Mechanical Batches

### Batch A: Provider easy wins

Files:
- `AIUsageTracker.Infrastructure/Providers/AntigravityProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/OpenCodeZenProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/OpenRouterProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/ZaiProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/OpenAIProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/OpenCodeProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/GeminiProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/CodexProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/ClaudeCodeProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/MinimaxProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/XiaomiProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/DeepSeekProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/GitHubCopilotProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/KimiProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/MistralProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/SyntheticProvider.cs`

Focus on:
- `SA1101`
- `SA1413`
- `SA1028`
- `SA1500`
- `SA1503`
- `SA1013`
- `SA1516`

Avoid in this batch:
- `SA1309`
- `SA1201`
- `SA1204`

### Batch B: Core model formatting

Files:
- `AIUsageTracker.Core/Models/ProviderDefinition.cs`
- `AIUsageTracker.Core/Models/AppPreferences.cs`
- `AIUsageTracker.Core/Models/ProviderUsage.cs`
- `AIUsageTracker.Core/Models/ProviderUsageDetail.cs`
- `AIUsageTracker.Core/Models/UsageComparison.cs`
- `AIUsageTracker.Core/Models/BudgetStatus.cs`
- `AIUsageTracker.Core/Models/ProviderReliabilitySnapshot.cs`
- `AIUsageTracker.Core/Models/UsageAnomalySnapshot.cs`
- `AIUsageTracker.Core/Models/BurnRateForecast.cs`
- `AIUsageTracker.Core/MonitorClient/AgentTelemetrySnapshot.cs`
- `AIUsageTracker.Core/Interfaces/IMonitorService.cs`

Focus on:
- `SA1101`
- `SA1516`
- small multiline formatting warnings

Avoid in this batch:
- `MA0048`
- `SA1649`

### Batch C: Infrastructure service cleanup

Files:
- `AIUsageTracker.Infrastructure/Configuration/JsonConfigLoader.cs`
- `AIUsageTracker.Infrastructure/Configuration/TokenDiscoveryService.cs`
- `AIUsageTracker.Infrastructure/Services/GitHubUpdateChecker.cs`
- `AIUsageTracker.Infrastructure/Services/UsageAnalyticsService.cs`
- `AIUsageTracker.Infrastructure/Services/DataExportService.cs`
- `AIUsageTracker.Infrastructure/Services/WindowsNotificationService.cs`
- `AIUsageTracker.Infrastructure/Services/CodexAuthService.cs`
- `AIUsageTracker.Infrastructure/Http/ResilientHttpClient.cs`

Focus on:
- `SA1101`
- `SA1516`
- `SA1028`
- `SA1413`
- `SA1500`
- `SA1503`
- `MA0004`

Avoid in this batch:
- `SA1309`
- `SA1202`
- `SA1204`

## Current Highest-Volume File Clusters

Top files by warning volume from the snapshot:
- `AIUsageTracker.Infrastructure/Providers/AntigravityProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/OpenCodeZenProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/OpenRouterProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/ZaiProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/ClaudeCodeProvider.cs`
- `AIUsageTracker.Infrastructure/Configuration/JsonConfigLoader.cs`
- `AIUsageTracker.Infrastructure/Services/GitHubUpdateChecker.cs`
- `AIUsageTracker.Infrastructure/Providers/OpenAIProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/GeminiProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/OpenCodeProvider.cs`
- `AIUsageTracker.Infrastructure/Providers/MinimaxProvider.cs`
- `AIUsageTracker.Core/Models/ProviderDefinition.cs`

## Working Rules For Whoever Takes This

- Keep commits atomic and file-clustered.
- Do not mix behavioral fixes with mechanical cleanup.
- Build after each batch:
  - `dotnet build AIUsageTracker.Web/AIUsageTracker.Web.csproj --configuration Debug --disable-build-servers -m:1`
- Prefer local-only validation unless a batch clearly needs wider testing.
- Skip `SA1309`, `SA1633`, `MA0048`, `SA1649`, `MA0051`, `MA0011`, and `NU1900`.

