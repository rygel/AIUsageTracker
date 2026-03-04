# TODO

## Current Status (Updated: 2026-03-04)

### Recently Completed
- **Provider Base Class** (P1): Created `ProviderBase` abstract class, refactored all 18 providers
- **HTTP Retry Policy** (P1): Created `ResilientHttpClient` with Polly policies
- **Provider Registration** (P2): Created `ProviderRegistrationExtensions.cs` with assembly scanning

### Up Next
All architecture streamlining tasks completed!

---

## Feature Backlog

- [x] Slim UI logging migration (Priority: P1, Effort: M): Replace ad-hoc `Debug.WriteLine`/`Console.WriteLine` diagnostics with `ILogger` (structured levels, timestamped output, centralized configuration).
- [x] Monitor logging format unification (Priority: P1, Effort: S/M): Migrate Monitor `[DIAG]`/custom diagnostic output to `ILogger` with timestamped, structured logs so Monitor and Slim UI diagnostics use one consistent format.
- [x] Burn-rate forecasting (Priority: P1, Effort: M): Estimate days until quota or budget exhaustion from recent usage trends.
- [x] Anomaly detection (experimental) (Priority: P1, Effort: M): Detect and flag unusual spikes or drops in provider usage.
- [x] Provider reliability panel (Priority: P2, Effort: M): Show provider latency, failure rate, and last successful sync timestamp.
- [ ] OpenCode CLI local provider (Priority: P2, Effort: M): Read detailed usage data from `opencode stats` CLI (sessions, messages, per-model breakdown, daily history) instead of just API credits endpoint
- [ ] Budget policies (Priority: P2, Effort: M): Add weekly/monthly provider budget caps with warning levels and optional soft-lock behavior.
- [ ] Comparison views (Priority: P3, Effort: S/M): Add period-over-period comparisons (day/week/month) and provider leaderboard by cost and growth.
- [ ] Data portability (Priority: P3, Effort: S): Support CSV/JSON export and import, plus scheduled encrypted SQLite backups.
- [ ] Plugin-style provider SDK (Priority: P3, Effort: L): Add a provider extension model with shared auth/HTTP/parsing helpers and conformance tests.
- [ ] Alert rules and notifications (Priority: P4, Effort: S/M): Add per-provider thresholds for remaining quota, spend percentage, and API failures with desktop and webhook notifications.

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
- Found 70+ occurrences of `IsAvailable = false` across providers
- Found 15+ `CreateUnavailableUsage` methods with 90%+ identical code
- Found 36+ generic `catch (Exception ex)` blocks without specific handling

**HTTP Client Duplication:**
- Found 29+ `private readonly HttpClient _httpClient` declarations
- Each provider manages its own HttpClient lifecycle
- No centralized retry or resilience policies

**Test Setup Duplication:**
- Found identical mock setup patterns in 15+ provider test files
- HttpClient mocking, logger mocking repeated everywhere

### Recommended Implementation Order

1. ~~**Provider Base Class**~~ - ✅ COMPLETED
2. ~~**HTTP Retry Policy**~~ - ✅ COMPLETED  
3. ~~**Provider Registration**~~ - ✅ COMPLETED
4. ~~**Test Base Classes**~~ - ✅ COMPLETED
5. ~~**Configuration Validation**~~ - ✅ COMPLETED
6. ~~**DateTime & Logging**~~ - ✅ COMPLETED
7. ~~**Dead Code Removal**~~ - ✅ COMPLETED

## All Architecture Streamlining Tasks Completed!

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

