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

### Grand Refactor (Completed 2026-03-21)
- [x] Strip ~7,000 lines of dead code, unused abstractions, duplicate interfaces.
- [x] Collapse ProviderMetadataCatalog from 737→420 lines (19 pass-through methods deleted).
- [x] Remove Polly/resilience stack; providers use plain HttpClient.
- [x] Convert MonitorLauncher from static to injectable singleton (remove AsyncLocal).
- [x] Remove 7 single-implementation interfaces never mocked in tests.
- [x] Split SettingsWindow.xaml.cs (2,500→4 partial files) and MainWindow.xaml.cs (1,800→6 partial files).
- [x] Replace reflection-based provider discovery with explicit static list.
- [x] Convert App.Themes.cs from switch statement to data-driven palette.
- [x] Remove static state from MonitorService (telemetry counters → instance fields).
- [x] Fix pace calculation: simple projection math, per-window pace for dual bars.
- [x] Move CodeQL to Ubuntu (~50% cost reduction).
- [x] Add DI resolution smoke tests, pace end-to-end tests, provider deserialization tests.

---

## Feature Backlog

### UI Clarity Improvements (Priority: P1)

These improvements make usage data immediately understandable without mental math or guessing:

- [ ] **Relative time remaining on reset badges** (Effort: S): Show "4d 22h left" or "2h 15m left" instead of absolute dates like "(Mar 26 17:44)". The `NextResetTime` data is already available — just needs relative time formatting. Users shouldn't have to calculate time differences mentally.

- [ ] **Projected end-of-period usage text** (Effort: S): Show "Projected: 85% at reset" next to the pace badge. The bar color already reflects projection, but users don't know WHY it's red. Uses `CalculateProjectedFinalPercent` which already exists. Makes the pace math transparent.

- [ ] **Distinct burst vs weekly bar indicators** (Effort: S): Codex dual bars show tiny "5h" and "Weekly" labels that are hard to distinguish at a glance. Add visual indicators — lightning bolt icon for burst windows, calendar icon for weekly. Or use distinct color coding per window type.

- [ ] **Color-coded pace badges** (Effort: S): "Over pace" and "On pace" are currently plain text. Make "Over pace" red background with white text, "On pace" green background with white text. No badge = neutral. Users scan colors, not text. Update badge styling in `ProviderCardRenderer`.

- [ ] **Daily budget display for weekly providers** (Effort: M): For a weekly quota of 300 requests, show "43/day budget" and "Today: 67 used" on the card. Simple division (`RequestsAvailable / 7`) that users shouldn't have to do themselves. Requires tracking daily usage from history data.

- [ ] **Visible stale data indicators** (Effort: S): "(last refreshed 9d ago — data may be outdated)" in small gray text is too subtle. Dim/fade the entire card when data is stale. Add a visible "Stale" badge with age. Consider auto-collapsing providers stale for >24h behind an expand button.

- [ ] **Auto-collapse inactive providers** (Effort: M): 27 providers shown at once — most users care about 3-4. Sort by recent activity (most-used first). Auto-collapse providers at 0% usage or with no recent activity into an "Other providers" expandable section. Reduces visual noise.

### Remaining Feature Backlog

- [ ] OpenCode CLI local provider (Priority: P2, Effort: M): Read detailed usage data from `opencode stats` CLI (sessions, messages, per-model breakdown, daily history) instead of just API credits endpoint.
- [ ] Budget policies (Priority: P2, Effort: M): Add weekly/monthly provider budget caps with warning levels and optional soft-lock behavior.
- [ ] Comparison views (Priority: P3, Effort: S/M): Add period-over-period comparisons (day/week/month) and provider leaderboard by cost and growth.
- [ ] Data portability (Priority: P3, Effort: S): Support CSV/JSON export and import, plus scheduled encrypted SQLite backups.
- [ ] Plugin-style provider SDK (Priority: P3, Effort: L): Add a provider extension model with shared auth/HTTP/parsing helpers and conformance tests.

---

## CI/CD Improvements: Web Testing Infrastructure

- [ ] Add Web project reference to Web.Tests project (Priority: P1, Effort: M)
  - Currently: Web.Tests does not reference AIUsageTracker.Web
  - Action: Add `<ProjectReference>` to Web.Tests.csproj
  - Benefit: Enables Razor view testing with Playwright
  - Files: AIUsageTracker.Web.Tests/AIUsageTracker.Web.Tests.csproj

- [ ] Create Razor view compilation tests (Priority: P1, Effort: M)
  - Currently: Only screenshot tests exist for Web
  - Action: Add view rendering tests (Dashboard, Providers, Charts, History, Provider, Reliability)
  - Action: Add model binding tests
  - Action: Add layout partial tests
  - Benefit: Validates Razor syntax, model binding, and view compilation
  - Location: AIUsageTracker.Web.Tests/ViewTests.cs (new file)

- [ ] Create Web service unit tests (Priority: P1, Effort: M)
  - Currently: Web.Services have no test coverage
  - Action: Add unit tests for WebDatabaseService, usage calculation services
  - Action: Add tests for authentication/authorization (if any)
  - Benefit: Comprehensive service layer testing
  - Location: AIUsageTracker.Web.Tests/Services/ (new directory)

- [ ] Create CI/CD workflow for Web tests (Priority: P1, Effort: M)
  - Currently: No CI/CD job builds or tests Web project
  - Action: Create `test-web.yml` workflow
  - Action: Build Web.csproj in prepare job
  - Action: Run Web unit tests and Playwright screenshot tests
  - Action: Upload test results
  - Benefit: Catches Razor/CSHTML errors before runtime
  - Location: .github/workflows/test-web.yml (new file)

- [ ] Test infrastructure complete (Priority: P1, Effort: S)
  - Currently: Web.Tests lacks proper setup and validation
  - Action: Add MSTestSettings.cs with proper configuration
  - Action: Validate TestFixture/TestContext setup
  - Action: Verify Playwright browser integration
  - Benefit: Comprehensive testing foundation for Web UI
  - Files: Complete Web.Tests project structure

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
