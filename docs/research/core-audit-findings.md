# Core Models & Interfaces Audit: Over-Abstraction & Dead Code

Date: 2026-06-11
Cycle: 16
Status: Draft — pending owner approval

## Summary

| Metric | Value |
|--------|-------|
| Total interfaces examined | 12 |
| Single-impl, no-mock interfaces | 3 |
| Dead types found | 3 |
| Estimated line reduction | ~136 lines |

The Core project at ~6,492 lines is already lean. No significant wrapper/adapter dead weight exists — the codebase follows AGENTS.md principles well.

## Interface Analysis

| Interface | Implementations | Mocked in Tests? | Recommendation |
|---|---|---|---|
| `IAppPathProvider` | 1 (DefaultAppPathProvider) | Yes — 30+ mock sites | **Keep** |
| `IConfigLoader` | 1 (JsonConfigLoader) | Yes — 3 test classes | **Keep** |
| `IDataExportService` | 2 (DataExportService, NoOpDataExportService) | No | **Keep** — two impls |
| `IGitHubAuthService` | 1 (GitHubAuthService) | No (test stubs in DI tests) | **Keep** |
| `IMonitorService` | 1 (MonitorService) | Yes — 3 test classes | **Keep** |
| `INotificationService` | 2 (WindowsNotificationService, NoOpNotificationService) | Yes — StartupAntiHammerTests | **Keep** |
| `IPreferencesStore` | 1 (PreferencesStore) | **No** | **Candidate** |
| `IProviderDiscoveryService` | 1 (ProviderDiscoveryService) | Yes — 4 test classes | **Keep** |
| `IProviderService` | 18+ providers via ProviderBase | Yes — 1 mock site | **Keep** — polymorphic |
| `IUsageAnalyticsService` | 1 (UsageAnalyticsService) | **No** | **Candidate** |
| `IWebDatabaseRepository` | 1 (WebDatabaseService) | Yes — 2 test classes | **Keep** |
| `IMonitorLauncher` | 1 (MonitorLauncher) | **No** | **Candidate** |

## Candidate Details

### 1. IPreferencesStore — Needs Careful Migration

- **File**: `AIUsageTracker.Core/Interfaces/IPreferencesStore.cs` (14 lines)
- **Implementation**: `AIUsageTracker.Infrastructure/Configuration/PreferencesStore.cs`
- **Why**: Single implementation, zero Moq mocks. Per AGENTS.md: "Interfaces are only for types that are mocked in tests."
- **Risk**: Low — straightforward find-and-replace of `IPreferencesStore` → `PreferencesStore` in DI and constructor sites
- **Lines saved**: ~14 (interface file)

### 2. IUsageAnalyticsService — Needs Careful Migration

- **File**: `AIUsageTracker.Core/Interfaces/IUsageAnalyticsService.cs` (29 lines)
- **Implementation**: `AIUsageTracker.Infrastructure/Services/UsageAnalyticsService.cs`
- **Why**: Single implementation, zero mocks. Consumed by Web pages `Index.cshtml.cs` and `Reliability.cshtml.cs`.
- **Risk**: Low — Web project references both Core and Infrastructure.
- **Lines saved**: ~29 (interface file)

### 3. IMonitorLauncher — Needs Careful Migration

- **File**: `AIUsageTracker.Core/MonitorClient/IMonitorLauncher.cs` (34 lines)
- **Implementation**: `AIUsageTracker.Core/MonitorClient/MonitorLauncher.cs`
- **Why**: Single implementation, zero mocks. Used by `MonitorLifecycleService` via DI.
- **Risk**: Low-Medium — DI registration changes needed in Slim UI, CLI, and Web.
- **Lines saved**: ~34 (interface file)

## Dead Code

### 1. PercentageValueSemantic — Safe to Remove

- **File**: `AIUsageTracker.Core/Models/PercentageValueSemantic.cs` (12 lines)
- **Evidence**: Zero references outside its own file.
- **Category**: Safe to Remove

### 2. BudgetPolicy — Safe to Remove

- **File**: `AIUsageTracker.Core/Models/BudgetPolicy.cs` (24 lines)
- **Evidence**: Zero references outside its own file. Scaffolded model never wired up.
- **Category**: Safe to Remove

### 3. ProviderResponseException — Safe to Remove

- **File**: `AIUsageTracker.Core/Exceptions/ProviderResponseException.cs` (23 lines)
- **Evidence**: Zero instantiations. All other 8 exception types in the hierarchy ARE thrown in `HttpRequestBuilderExtensions.cs`. This one is not. The `ProviderErrorType.InvalidResponseError` enum value it maps to is still used directly.
- **Category**: Safe to Remove

## False Positives (Verified Active)

| Item | Evidence |
|---|---|
| All 8 other exception types | Thrown in `HttpRequestBuilderExtensions.cs` |
| `BudgetStatus` / `BudgetPeriod` | Used in Web UI and `IUsageAnalyticsService` |
| `UsageComparison` | Referenced in `IUsageAnalyticsService` and `Index.cshtml.cs` |
| All `Agent*` types in MonitorClient | Active API contract models |
| All provider models | Heavily used across all projects |

## Quantified Savings

| Category | Items | Line Reduction |
|---|---|---|
| Safe to Remove | PercentageValueSemantic, BudgetPolicy, ProviderResponseException | ~59 lines |
| Needs Careful Migration | IPreferencesStore, IUsageAnalyticsService, IMonitorLauncher | ~77 lines |
| Keep (false positive) | All other interfaces and types | 0 |
| **Total** | | **~136 lines** |
