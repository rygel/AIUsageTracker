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

