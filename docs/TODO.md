# TODO

## Current Status (Updated: 2026-03-04)

### Recently Completed
- **Provider Exception Types** (P1): Created structured exception hierarchy with 8 specific exception types
- **HTTP Request Builder Extensions** (P2): Standardized HTTP request patterns with automatic exception mapping
- **Shared Helper Utilities** (P2): Created `ResetTimeParser` and enhanced `UsageMath` for common operations
- **Magic String Constants** (P3): Extracted API endpoints, HTTP headers, and error messages to constants
- **Provider Base Class** (P1): Created `ProviderBase` abstract class, refactored all 18 providers
- **HTTP Retry Policy** (P1): Created `ResilientHttpClient` with Polly policies
- **Provider Registration** (P2): Created `ProviderRegistrationExtensions.cs` with assembly scanning

### Up Next
All architecture streamlining tasks completed! See remaining feature backlog below.

### UI Runtime Reliability Backlog (Added: 2026-03-21)
- [ ] Slim UI multi-instance policy + guardrails (Priority: P1, Effort: M): Define supported behavior explicitly. Either enforce single-instance via named mutex (focus existing window and exit second launch) or allow multi-instance only in an explicit mode that prevents shared-state races (no preference writes, no monitor lifecycle ownership changes).
- [ ] Persist Slim UI diagnostics to file (Priority: P1, Effort: S): Add UI log file output under `%LOCALAPPDATA%\\AIUsageTracker\\logs\\ui_YYYY-MM-DD.log` with render-path checkpoints (`raw count`, `post-filter count`, empty-state branch, render exceptions) for post-incident debugging.
- [ ] Harden monitor duplicate-start guard (Priority: P1, Effort: S/M): Keep single active monitor process per user/session with explicit startup lock diagnostics and regression tests for concurrent launch attempts from multiple Slim UI processes.

### Cleanup Queue (Added: 2026-03-09)
- [ ] Formatter-first mechanical pass (Priority: P1, Effort: M): Run repo-wide formatting and style normalization to reduce the largest warning buckets (`IDE0065`, `IDE0161`, `SA1028`, `SA1507`).
- [ ] Initializer and layout consistency (Priority: P1, Effort: M): Fix multi-line initializer/list formatting and spacing (`SA1413`, `SA1117`, `SA1508`, `SA1516`).
- [ ] Member ordering normalization (Priority: P1, Effort: M): Resolve ordering/layout warnings (`SA1201`, `SA1202`, `SA1204`) in highest-churn files first.
- [ ] Semantic analyzer fixes (Priority: P1, Effort: M): Address correctness/readability warnings (`MA0074`, `MA0006`, `MA0004`) in non-UI/core paths first.
- [ ] File structure cleanup (Priority: P2, Effort: M): Split multi-type files and align file/type names (`SA1402`, `SA1649`).
  - **Progress (2026-03-11)**: Split `MonitorRefreshHealthSnapshot`, `HttpProviderTestBase`, and collection-definition classes into file-matching types to reduce `SA1402`/`SA1649` in Core and test infrastructure.
- [x] Guardrail hardening (Priority: P1, Effort: S): Add/strengthen pre-commit and pre-push checks to enforce `dotnet format` + analyzer hygiene.
  - **Completed**: `pre-commit-check.sh` now enforces whitespace/style verification on staged files; `pre-push-validation.ps1` now verifies formatting for files changed against `origin/develop` and supports optional strict analyzer gating.

#### High-Volume Target Files
- `AIUsageTracker.UI.Slim/App.xaml.cs`
- `AIUsageTracker.Tests/Mocks/MockProviderService.cs`
- `AIUsageTracker.UI.Slim/SettingsWindowDeterministicFixture.cs`
- `AIUsageTracker.UI.Slim/App.TrayIcon.cs`
- `AIUsageTracker.Monitor.Tests/ProviderRefreshServiceTests.cs`

### Analyzer Follow-Up
- [x] Add third-party analyzer packages (Priority: P1, Effort: S/M): Evaluate and enable `Microsoft.VisualStudio.Threading.Analyzers`, `Meziantou.Analyzer`, and optionally `StyleCop.Analyzers` with repo-specific severities after the current MSBuild/test invocation issues are stabilized.
  - Goal: catch sync-over-async, brittle async patterns, and consistency issues earlier in CI
  - Constraint: introduce in a controlled pass to avoid warning floods and masking real build/test regressions
  - **Completed**: PR #258 added StyleCop.Analyzers and enabled MA0048 + key StyleCop spacing rules. Subsequent PR enhanced VSTHRD200 (error) and MA0016 (error).

---

## Feature Backlog

- [x] Slim UI logging migration (Priority: P1, Effort: M): Replace ad-hoc `Debug.WriteLine`/`Console.WriteLine` diagnostics with `ILogger` (structured levels, timestamped output, centralized configuration).
- [x] Monitor logging format unification (Priority: P1, Effort: S/M): Migrate Monitor `[DIAG]`/custom diagnostic output to `ILogger` with timestamped, structured logs so Monitor and Slim UI diagnostics use one consistent format.
- [x] Burn-rate forecasting (Priority: P1, Effort: M): Estimate days until quota or budget exhaustion from recent usage trends.
- [x] Anomaly detection (experimental) (Priority: P1, Effort: M): Detect and flag unusual spikes or drops in provider usage.
- [x] Provider reliability panel (Priority: P2, Effort: M): Show provider latency, failure rate, and last successful sync timestamp.
- [x] OpenCode CLI local provider (Priority: P2, Effort: M): Read detailed usage data from `opencode stats` CLI (sessions, messages, per-model breakdown, daily history) instead of just API credits endpoint
  - **Completed**: Added `OpenCodeZenProvider` using `opencode stats --days 7 --models 10` with parsed detail rows and provider tests.
- [ ] Budget policies (Priority: P2, Effort: M): Add weekly/monthly provider budget caps with warning levels and optional soft-lock behavior.
- [ ] Comparison views (Priority: P3, Effort: S/M): Add period-over-period comparisons (day/week/month) and provider leaderboard by cost and growth.
- [ ] Data portability (Priority: P3, Effort: S): Support CSV/JSON export and import, plus scheduled encrypted SQLite backups.
- [x] Plugin-style provider SDK (Priority: P3, Effort: L): Add a provider extension model with shared auth/HTTP/parsing helpers and conformance tests.
  - **Completed**: Implemented `ProviderDiscoveryService`/`IProviderDiscoveryService`, shared provider metadata/discovery models, and associated infrastructure tests.

---

## CI/CD Improvements: Web Testing Infrastructure

- [x] Add Web project reference to Web.Tests project (Priority: P1, Effort: M)
  - Currently: Web.Tests does not reference AIUsageTracker.Web
  - Action: Add `<ProjectReference>` to Web.Tests.csproj
  - Benefit: Enables Razor view testing with Playwright
  - Files: AIUsageTracker.Web.Tests/AIUsageTracker.Web.Tests.csproj
  - **Completed**: `AIUsageTracker.Web.Tests.csproj` now references `..\AIUsageTracker.Web\AIUsageTracker.Web.csproj`.

- [x] Create Razor view compilation tests (Priority: P1, Effort: M)
  - Currently: Only screenshot tests exist for Web
  - Action: Add view rendering tests (Dashboard, Providers, Charts, History, Provider, Reliability)
  - Action: Add model binding tests
  - Action: Add layout partial tests
  - Benefit: Validates Razor syntax, model binding, and view compilation
  - Location: AIUsageTracker.Web.Tests/ViewTests.cs (new file)
  - **Completed**: `AIUsageTracker.Web.Tests/ViewTests.cs` covers route rendering, model-binding query parameters, and layout/navigation assertions.

- [x] Create Web service unit tests (Priority: P1, Effort: M)
  - Currently: Web.Services have no test coverage
  - Action: Add unit tests for WebDatabaseService, usage calculation services
  - Action: Add tests for authentication/authorization (if any)
  - Benefit: Comprehensive service layer testing
  - Location: AIUsageTracker.Web.Tests/Services/ (new directory)
  - **Completed**: Added `WebDatabaseServiceTests` covering database-unavailable behavior, latest-usage filtering, and usage-summary cache behavior.

- [x] Create CI/CD workflow for Web tests (Priority: P1, Effort: M)
  - Currently: No CI/CD job builds or tests Web project
  - Action: Create `test-web.yml` workflow
  - Action: Build Web.csproj in prepare job
  - Action: Run Web unit tests and Playwright screenshot tests
  - Action: Upload test results
  - Benefit: Catches Razor/CSHTML errors before runtime
  - Location: .github/workflows/test-web.yml (new file)
  - **Completed**: Implemented in consolidated `.github/workflows/tests.yml` (`web-tests-windows`) including build, web test suite, screenshot capture, and artifact upload.

- [x] Test infrastructure complete (Priority: P1, Effort: S)
  - Currently: Web.Tests lacks proper setup and validation
  - Action: Add MSTestSettings.cs with proper configuration
  - Action: Validate TestFixture/TestContext setup
  - Action: Verify Playwright browser integration
  - Benefit: Comprehensive testing foundation for Web UI
  - Files: Complete Web.Tests project structure
  - **Completed**: Added `MSTestSettings.cs`, `WebTestBase`/`KestrelWebApplicationFactory`, and Playwright-backed screenshot tests.

---

## Migration Plan: Provider-Owned Usage Details (Strict Contract)

- [x] Freeze strict detail schema in Core (Priority: P1, Effort: S): Keep `ProviderUsageDetail.DetailType`, add `WindowKind` (`Primary`, `Secondary`, `Spark`, `None`), and define required-field rules for every emitted detail row.
- [x] Make provider output the single source of truth (Priority: P1, Effort: M): Update all providers that emit `Details` to always set `DetailType`/`WindowKind`; treat missing or `Unknown` values as provider bugs.
- [x] Remove all runtime heuristics in clients (Priority: P1, Effort: M): Delete name-based fallback parsing from Slim UI, Web UI, and CLI; render/filter only from typed fields.
- [x] Remove fallback helpers from Core model (Priority: P1, Effort: S): Delete string-name classification helpers once providers and clients are fully typed to avoid dual logic paths.
- [x] Add strict runtime validation in Monitor (Priority: P1, Effort: M): Validate detail contract during refresh and return explicit provider error states when typed fields are invalid.
- [x] Add/extend tests for strict mode (Priority: P1, Effort: M): Add tests that fail when providers emit invalid detail typing and tests that assert typed rendering paths only.
- [x] Enforce in CI (Priority: P2, Effort: S): Add a guard test/job that fails if any provider output includes untyped/invalid detail rows.
- [x] Legacy history strategy without runtime fallback (Priority: P2, Effort: S/M): Keep old rows as historical data, but do not add runtime backfill heuristics; optional one-off migration script only if needed.
- [x] Document contract and implementation checklist (Priority: P2, Effort: S): Update docs with strict expectations, rollout steps, and provider implementation examples.

## Resilience Plan: Monitor Startup and Port Binding

- [x] Handle bind race at runtime (Priority: P1, Effort: M): Wrap startup bind in retry logic for `AddressInUseException`; on collision, select a new port and retry with bounded attempts and structured logs.
- [x] Move monitor metadata write after successful bind (Priority: P1, Effort: S): Do not write `monitor.json` before the monitor is actually listening; publish PID/port only after startup succeeds.
- [x] Add startup synchronization (Priority: P1, Effort: M): Add a machine-local startup lock (mutex or lock file) so concurrent launch attempts cannot race each other.
- [x] Add stale metadata guard (Priority: P1, Effort: S): On client side, if `monitor.json` PID is dead or health fails, invalidate stale metadata and force fresh discovery/start flow.
- [x] Add explicit startup failure reporting (Priority: P1, Effort: S): Write startup failure reason to monitor log and `monitor.json` error field when startup aborts.
- [x] Tighten launcher idempotency (Priority: P2, Effort: M): In `MonitorLauncher`, avoid duplicate starts by re-checking health/PID under lock before and after spawn.
- [x] Add integration test for port-collision recovery (Priority: P1, Effort: M): Simulate occupied preferred port and verify monitor recovers on alternate port without crash.
- [x] Add integration test for concurrent starts (Priority: P1, Effort: M): Trigger parallel start attempts and assert exactly one running monitor with valid `monitor.json`.
 - [x] Add regression test for stale metadata recovery (Priority: P2, Effort: S): Seed dead PID/old port in `monitor.json` and verify client reconnects without persistent stale-state warnings.

## Architecture Streamlining Opportunities

Identified during code review on 2026-03-03. These are areas where the codebase has duplication or inconsistent patterns that could be streamlined.

### High Priority Streamlining Tasks

- [x] Implement Provider SDK Pattern (Priority: P1, Effort: M): Centralize authentication discovery (environment variables, auth files) into a dedicated `ProviderDiscoveryService` and refactor `ProviderBase` to use it.
  - Benefit: Reduces boilerplate in concrete providers, standardizes auth resolution, simplifies adding new providers.
  - **Completed**: Created `ProviderDiscoveryService`, refactored `ProviderBase`, and updated all core providers.

- [x] Centralized Resilience & Observability Strategy (Priority: P1, Effort: M): Introduce a global `IResilienceProvider` managing named Polly policies (retry, circuit breaker) for all network requests.
  - Benefit: Tailored retry strategies per provider, global circuit breaker, and enhanced failure observability.
  - **Completed**: Implemented `ResilienceProvider` and integrated into `ResilientHttpClient`.

- [x] Push-Based Updates (Priority: P2, Effort: M): Implement real-time notifications using SignalR to replace or augment polling-only data fetching in the Slim UI.
  - Benefit: Instant UI updates upon successful refresh, reduced latency, and lower resource usage.
  - **Completed**: Added `UsageHub` to Monitor and SignalR client to Slim UI.

- [x] Create Provider Base Class (Priority: P1, Effort: M): Create `ProviderBase` abstract class in `AIUsageTracker.Core` to eliminate duplicate `CreateUnavailableUsage` methods (~300 lines across 15+ providers). Base class should provide standard error handling, logging, and unavailable usage creation.
  - Locations: All providers in `AIUsageTracker.Infrastructure/Providers/`
  - Pattern: Each provider has `CreateUnavailableUsage(string message)` with nearly identical implementation
  - Benefit: Centralized error handling, consistent unavailable states, reduced code duplication
  - **Completed**: Created `ProviderBase` abstract class with `CreateUnavailableUsage`, `CreateUnavailableUsageFromStatus`, `CreateUnavailableUsageFromException` methods. Refactored all 18 providers to use it.

- [x] Standardize HTTP Retry Policy (Priority: P1, Effort: M): Create `ResilientHttpClient` wrapper with Polly - DONE policies (exponential backoff, circuit breaker, timeout) to replace inconsistent retry handling across providers.
  - Current: Some providers don't retry, others have custom implementations
  - Locations: All HTTP-dependent providers
  - Benefit: Consistent resilience, centralized configuration, better reliability
  - **Completed**: Created `ResilientHttpClient` with Polly policies, retry (3 attempts, exponential backoff), circuit breaker (5 failures, 30s break), timeout. Integrated into Monitor and CLI.

### Future Architectural Enhancements

- [x] Background Job Orchestration (Priority: P2, Effort: M): Introduce a formal internal job scheduler for periodic refreshes, data pruning, and maintenance tasks.
  - Benefit: Support for job prioritization (manual "Force Refresh" vs background), better concurrency control, and easier task management.
  - Location: `AIUsageTracker.Monitor/Services/`
  - **Completed**: Added `MonitorJobScheduler` (priority queues + recurring registration), moved periodic refresh scheduling to scheduler, and routed manual refresh requests through high-priority queue paths.
  - **Hardening update (2026-03-10)**: Added explicit `QueueForceRefresh` API path (manual refresh bypasses circuit breaker), request-aware manual refresh coalescing (dedupe identical requests while allowing distinct scopes), and stabilized scheduler timing tests.

- [ ] Standardized Data Validation & Transformation (Pipe & Filter) (Priority: P2, Effort: M): Implement a "Pipe & Filter" architecture for processing usage data before it is stored in the database.
  - Benefit: Rejects invalid data, normalizes units/percentages, and can handle privacy redacting centrally.
  - Location: `AIUsageTracker.Core/Services/ProviderManager.cs`
  - **Phase 1 Completed**: Added `ProviderUsageProcessingPipeline` in Monitor and routed refresh results through a centralized processing pass (active-provider filtering, detail-contract validation mapping, numeric/timestamp normalization, and privacy redaction of sensitive fields before persistence).

- [x] Observability & Distributed Tracing (Priority: P3, Effort: S/M): Leverage `System.Diagnostics.DiagnosticSource` and `Activity` to implement distributed tracing across the system.
  - Benefit: Correlation of refresh requests across UI, Monitor, and specific provider services, making diagnosis of failures significantly easier.
  - Location: Core components and infrastructure services.
  - **Completed**: Added `ActivitySource` spans for monitor lifecycle and refresh flows in `MonitorService`, `MonitorProcessService`, and `ProviderRefreshService` with outcome/status tags.

### Medium Priority Streamlining Tasks

- [x] Consolidate Provider Registration (Priority: P2, Effort: S): Use assembly scanning to auto-register providers instead of manual registration in `Program.cs` files.
  - Current: Each provider manually registered in DI container
  - Pattern: `services.AddSingleton<IProviderService, XProvider>()` repeated 15+ times
  - Benefit: Adding new provider requires zero registration code changes
  - **Completed**: Created `ProviderRegistrationExtensions.cs` with `AddProvidersFromAssembly()` method, refactored `ProviderRefreshService` for DI injection, all 162 tests pass

- [x] Standardize Configuration Validation (Priority: P2, Effort: S): Add validation attributes to `ProviderConfig` model for automatic validation instead of manual checks in each provider.
  - Current: Each provider validates `ProviderConfig` differently
  - Locations: All providers with `GetUsageAsync(ProviderConfig config)`
  - Benefit: Declarative validation, consistent error messages, less boilerplate
  - **Completed**: Added DataAnnotations to ProviderConfig (Required, StringLength, Range, RegularExpression for validation).

- [x] Create Test Base Classes (Priority: P2, Effort: S): Create `ProviderTestBase<T>` to eliminate duplicate test setup code across provider tests.
  - Pattern: `Mock<HttpMessageHandler>`, `HttpClient`, `ILogger<T>` setup repeated in every provider test
  - Locations: All test files in `AIUsageTracker.Tests/Infrastructure/Providers/`
  - Benefit: Reduced test boilerplate, consistent test patterns
  - **Completed**: Created `ProviderTestBase<T>` and `HttpProviderTestBase<T>` base classes with common setup, logger, config, and HTTP mocking utilities.

### Low Priority Streamlining Tasks

- [x] Unify DateTime Handling (Priority: P3, Effort: S): Standardize on `DateTime.UtcNow` across all providers with extension methods for display conversion.
  - Current: Mix of `DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset`
  - Locations: Provider implementations, database operations, UI display
  - Benefit: Consistent time handling, no timezone bugs
  - **Completed**: Created `DateTimeExtensions.cs` in Core with helper methods for UTC conversion, Unix timestamp handling, and ISO8601 formatting.

- [x] Standardize Logging Patterns (Priority: P3, Effort: S): Define logging standards and use Source Generators for high-performance logging.
  - Current: Inconsistent logging patterns (some use structured, some use interpolated strings)
  - Locations: All providers and services
  - Benefit: Consistent logs, better performance, easier filtering
  - **Completed**: Created `ProviderLoggingExtensions.cs` with `[LoggerMessage]` source-generated methods. Enabled `EnableLoggingGenerator` in Infrastructure csproj.

- [x] Remove Dead Code (Priority: P3, Effort: S): Scan for and remove unused using statements, private methods never called, commented-out code blocks, duplicate constants.
  - Benefit: Cleaner codebase, faster builds, easier maintenance
  - **Completed**: Build shows no unused code warnings; existing code comments are purposeful documentation.

### Code Duplication Analysis Summary

**Provider Error Handling Duplication:**
- ~~Found 70+ occurrences of `IsAvailable = false` across providers~~ ✅ Addressed with ProviderBase
- ~~Found 15+ `CreateUnavailableUsage` methods with 90%+ identical code~~ ✅ Consolidated in ProviderBase
- ~~Found 36+ generic `catch (Exception ex)` blocks without specific handling~~ ✅ Now mapped to specific ProviderException types

**HTTP Client Duplication:**
- ~~Found 29+ `private readonly HttpClient _httpClient` declarations~~ ✅ Now use ResilientHttpClient
- ~~Each provider manages its own HttpClient lifecycle~~ ✅ Centralized via DI
- ~~No centralized retry or resilience policies~~ ✅ Added Polly policies

**Request Creation Duplication:**
- ~~Found 15+ occurrences of `new HttpRequestMessage(HttpMethod.Get, url)`~~ ✅ Use HttpRequestBuilderExtensions
- ~~Found 15+ Bearer token header setups~~ ✅ Centralized in CreateBearerRequest

**Reset Time Parsing Duplication:**
- ~~Found 10+ providers with custom reset time parsing logic~~ ✅ Use ResetTimeParser utility
- ~~Multiple Unix timestamp conversion implementations~~ ✅ Centralized in ResetTimeParser

**Test Setup Duplication:**
- Found identical mock setup patterns in 15+ provider test files
- HttpClient mocking, logger mocking repeated everywhere
- **Status**: Partially addressed with ProviderTestBase, could be further improved

### Recommended Implementation Order

1. ~~**Provider Base Class**~~ - ✅ COMPLETED
2. ~~**HTTP Retry Policy**~~ - ✅ COMPLETED  
3. ~~**Provider Registration**~~ - ✅ COMPLETED
4. ~~**Test Base Classes**~~ - ✅ COMPLETED
5. ~~**Configuration Validation**~~ - ✅ COMPLETED
6. ~~**DateTime & Logging**~~ - ✅ COMPLETED
7. ~~**Dead Code Removal**~~ - ✅ COMPLETED
8. ~~**Provider Exception Types**~~ - ✅ COMPLETED (P1 Task 3)
9. ~~**HTTP Request Builder Extensions**~~ - ✅ COMPLETED (P2 Task 1)
10. ~~**Shared Helper Utilities**~~ - ✅ COMPLETED (P2 Task 2)
11. ~~**Magic String Constants**~~ - ✅ COMPLETED (P3 Task 1)

## All Architecture Streamlining Tasks Completed! 🎉

**Summary of Improvements:**
- **174 lines of duplicate code eliminated** via ProviderBase
- **15+ providers refactored** to use standardized patterns
- **8 specific exception types** for targeted error handling
- **4 standardized HTTP request methods** for consistent API calls
- **10+ reset time parsing utilities** for consistent date handling
- **3 constants files** with 250+ constants for endpoints, headers, and messages
- **All 162 unit tests passing**

## Integration & E2E Test Gaps (Added: 2026-03-19)

These gaps were identified after the `provider_history` dedup gate crashed in production with a Dapper `Int32`/`Int64` type mismatch — a bug class that only real-SQLite integration tests can catch.

- [ ] **Read-path Dapper mapping tests** (Priority: P1, Effort: S): Add real-SQLite integration tests for `GetHistoryAsync`, `GetHistoryByProviderAsync`, and `GetRecentHistoryAsync` to verify Dapper positional-constructor type mapping for all columns.
  - Motivation: Same `Int64` vs `Int32` mismatch that crashed the dedup gate could exist in any method that Dapper-maps a DB record.
  - Location: `AIUsageTracker.Tests/Services/UsageDatabaseReadTests.cs` (new file)
  - **In-progress**: `test/database-and-pipeline-integration`

- [ ] **Full pipeline integration test** (Priority: P1, Effort: M): Inject real provider output through `ProviderUsageProcessingPipeline` → `StoreHistoryAsync` → `GetLatestHistoryAsync` and assert that what the UI sees matches the original provider data.
  - Motivation: Validates the full data path end-to-end; a type or mapping regression anywhere would be caught.
  - Location: `AIUsageTracker.Tests/Services/UsageDatabasePipelineTests.cs` (new file)
  - **In-progress**: `test/database-and-pipeline-integration`

- [ ] **Circuit-breaker write suppression test** (Priority: P2, Effort: M): Verify that a tripped circuit breaker (provider returning errors repeatedly) suppresses `StoreHistoryAsync` calls over multiple refresh cycles, and that history stays stable.
  - Motivation: Circuit breaker state must propagate correctly all the way to DB writes, not just be visible in the ProviderUsage object.
  - Location: `AIUsageTracker.Tests/Services/UsageDatabaseCircuitBreakerTests.cs` (new file)
  - **In-progress**: `test/database-and-pipeline-integration`

- [ ] **Stale-data detection E2E test** (Priority: P1, Effort: S): Store a row with `fetched_at` older than `StaleDataThreshold`, call `GetLatestHistoryAsync`, and assert `IsStale = true` with the correct description suffix.
  - Motivation: The dedup gate changes `fetched_at` semantics (now "last confirmed current"); must verify stale detection still fires correctly.
  - Location: `AIUsageTracker.Tests/Services/UsageDatabaseReadTests.cs` (new file, same class)
  - **In-progress**: `test/database-and-pipeline-integration`

---

## CI/CD Pipeline Optimization Opportunities

### Phase 1 - Quick Wins (High Impact, Low Effort)

- [x] Security scanning workflow (Priority: P1, Effort: S): Add dependency vulnerability scanning with `dotnet list package --vulnerable`
  - Run on PR and scheduled basis
  - Upload results to GitHub Security tab
  - Benefit: Catch vulnerable dependencies before production

- [x] Conditional workflow skipping (Priority: P1, Effort: S): Skip unnecessary workflows for documentation-only changes
  - Use `paths-filter` or `paths-ignore` more aggressively
  - Skip builds when only markdown files change
  - Benefit: 30-40% reduction in CI minutes for docs PRs

- [x] Cache optimization (Priority: P1, Effort: S): Add Playwright browser caching, Docker layer caching
  - Cache `~/.cache/ms-playwright` separately
  - Use `restore-keys` for fallback cache hits
  - Benefit: Faster workflow execution

- [x] Build artifact compression (Priority: P2, Effort: S): Compress artifacts before upload, reduce retention days
  - Use `tar.gz` compression before upload
  - Reduce retention from 7 to 3 days for non-release builds
  - Benefit: Faster uploads, lower storage costs

- [x] Add aggressive timeout safeguards (Priority: P1, Effort: S): Prevent runaway CI jobs
  - All 12 workflows now have explicit timeout-minutes
  - Reduced from default 6 hours to 2-15 minutes per job
  - Documented timeout strategy at docs/CI_CD_TIMEOUTS.md
  - Benefit: Prevents hung jobs, reduces CI cost waste

### Phase 2 - Medium Effort Improvements

- [x] Code coverage reporting (Priority: P2, Effort: M): Add code coverage collection and reporting
  - Use `dotnet test --collect:"XPlat Code Coverage"`
  - Upload to Codecov or similar service
  - Add coverage badges to README
  - Benefit: Track coverage trends, ensure test quality

- [x] PR size limit warning (Priority: P2, Effort: S): Warn on large PRs over 1000 lines
  - Add workflow step to calculate diff stats
  - Post comment on PR if too large
  - Benefit: Encourage focused, reviewable PRs

- [ ] Notification integration (Priority: P2, Effort: S): Add Slack/Discord notifications for failed builds
  - Notify on main/develop branch failures only
  - Include link to failed run and error summary
  - Benefit: Faster incident response

- [ ] Reusable workflow templates (Priority: P2, Effort: M): Create `reusable-test.yml` for common test patterns
  - Parameterize test filter, timeout, OS
  - Replace duplicated test job definitions
  - Benefit: Single source of truth for test execution

### Phase 3 - Larger Projects

- [x] Matrix builds for cross-platform testing (Priority: P3, Effort: M): Run tests on Windows, Ubuntu
  - Test matrix: OS × platform
  - Tests Core and Infrastructure on both Windows and Linux
  - Skips Windows-specific tests on Linux (target framework mismatch)
  - Benefit: Catch OS-specific bugs early

- [x] Automated dependency updates (Priority: P3, Effort: M): Weekly automated PRs for dependency updates
  - Runs weekly on Monday at 2 AM
  - Checks for outdated NuGet packages
  - Creates PR with dependency updates automatically
  - Labels PRs with 'dependencies' and 'automated'
  - Benefit: Keep dependencies current without manual work

- [x] Build performance monitoring (Priority: P3, Effort: M): Track and alert on build time regressions
  - GitHub-native solution using GitHub's built-in APIs (no external services)
  - Compares PR build times against main branch baseline
  - Posts performance report comments on PRs with visual indicators
  - Alerts if build time increases >20%
  - Shows metrics in job summary
  - Benefit: Proactively detect performance regressions

- [x] Full workflow refactoring (Priority: P3, Effort: L): Consolidate similar workflows, remove redundancies
  - Merged test.yml and cross-platform-tests.yml into tests.yml
  - Removed unused reusable-test.yml
  - 5 parallel jobs with clear dependencies
  - Unified trigger conditions
  - Standardized naming and structure
  - Updated build-performance-monitor.yml references
  - Benefit: Easier maintenance, less confusion, clearer structure
