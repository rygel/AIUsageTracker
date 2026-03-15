# Changelog

## [Unreleased]

## [2.2.28-beta.41] - 2026-03-15

### Fixed
- **Z.ai Reset Timer**: When the 5-hour rolling window is fresh (0% used), the API returns the billing period end date (~8 days away) as `nextResetTime` instead of the rolling window close. The UI was showing "7 days 17 hours" for a limit that resets every 5 hours. Fixed by parsing `unit`/`number` fields from the limit item (`unit=3=hours, number=5` → 5-hour window) and displaying a "5h window" label instead of the misleading far-future date. When the window is active (tokens consumed), the actual window close time is shown as before.
- **Codex Spark Weekly Quota**: Spark's child card was showing the same weekly usage as the main Codex card (e.g. 2% remaining) instead of its own independent weekly quota (e.g. 81% remaining). Root cause: `Math.Max(sparkWindow.SecondaryUsedPercent, secondaryUsedPercent)` was combining Spark's independent weekly counter with the main Codex weekly counter. Fixed by using null-coalescing (`??`) to prefer Spark's own secondary window and fall back to the main weekly only when Spark has none.

## [2.2.28-beta.40] - 2026-03-15

### Fixed
- **Kimi Dual Bar Wrong Percentage**: Kimi card was showing 75% used when actual weekly usage was 25%. Root cause: `ProviderDualQuotaBucketPresentationCatalog` had a `?? orderedBuckets[1]` fallback that silently selected a second detail with the *same* `WindowKind` as the first, causing both bar segments to reference the same Rolling bucket. Removed the fallback entirely — dual bar now requires genuinely different `WindowKind`s; if no such pair exists, `TryGetPresentation` returns false and the card falls back to a single bar.
- **Kimi Duplicate Rolling Detail**: `KimiProvider` was adding a "Weekly Limit" detail from `data.Usage` *and* a "7d Limit" detail from `data.Limits` — both classified as `WindowKind.Rolling`. After the fallback removal above, the duplicate caused `TryGetPresentation` to find no different-kind second detail. Fixed by skipping the `data.Usage` weekly detail when `data.Limits` already contains a 7-day (Rolling) entry.
- **Dead Fallback Arm in UsageMath**: `GetEffectiveUsedPercent(ProviderUsageDetail)` had an unreachable arm that treated `PercentageSemantic.None` on a `QuotaWindow` detail as "remaining" and silently inverted the value. All providers now set `PercentageSemantic` explicitly, so this arm was dead code and a latent trap. Removed.
- **Codex Double-Inversion**: `CodexProvider` computed `UsedPercent = 100.0 - remainingPercent` where `remainingPercent` was already `100 - effectiveUsedPercent`, resulting in `100 - (100 - x) = x` — a harmless no-op but confusing. Simplified to use `effectiveUsedPercent` directly.
- **Status Text Round-Trip**: `GetQuotaPercentStatusText` was computing remaining as `100 - RemainingPercent` (i.e. `100 - (100 - UsedPercent)`) instead of reading `UsedPercent` directly. Fixed to use `UsedPercent` and `RemainingPercent` directly.

## [2.2.28-beta.39] - 2026-03-14

### Fixed
- **Codex Dual Bars**: Fixed both quota bars (5-hour burst + weekly rolling) not rendering on the Codex card. `NormalizeDetails` in the processing pipeline was dropping `PercentageValue` and `PercentageSemantic` when copying detail objects, causing `GetEffectiveUsedPercent` to return null for both windows and `TryGetPresentation` to bail out entirely.
- **Stale Detail Visual**: Cards whose provider details are marked stale now render at 65% opacity to signal outdated data.
- **Aggregate Fallback Text**: Provider cards in aggregate-parent mode now show "Quota details unavailable" instead of the misleading "Per-model quotas".

### Changed
- **Architecture Simplification**: Removed `ProviderCapabilityCatalog` (96-line pure pass-through layer — 15 forwarding wrappers with zero added logic). All 35+ call sites now reference `ProviderMetadataCatalog` directly. Removed `GetCanonicalProviderId` and `GetIconAssetName` pass-throughs from `ProviderVisualCatalog`.
- **Presentation Record Cleanup**: Removed `IsAggregateParent` from `ProviderCardPresentation` (set but never read externally). Added `IsStale`, `DualBucketPrimaryUsed`, `DualBucketSecondaryUsed` to the record so consumers don't re-derive them.
- **Repo Cleanup**: Moved documentation files from root into `docs/` (keeping README, CHANGELOG, CONTRIBUTING, LICENSE at root per GitHub conventions). Deleted stale session artifacts (`memory.md`, `task.md`, `problems.md`), legacy scripts (`fix_resources.js/py`), and accumulated build/test logs.
- **Warning Cleanup**: Eliminated CS8625, CS8604, MA0006, SA1122, MA0011, MA0009, CS0618, MA0016 warning categories across the solution. Added `.ConfigureAwait(false)` to non-UI async code.

## [2.2.28-beta.38] - 2026-03-14

### Fixed
- **Claude Code Timeout**: Timeout no longer leaks raw `[Error] Timeout after 25s` text into the UI; falls through to the standard error card path.

### Changed
- **Data Flow Refactor**: Consolidated aggregate child expansion into `ProviderUsageDisplayCatalog.ExpandSyntheticAggregateChildren`, eliminating duplicated logic across `MainWindow`, `MainViewModel`, and `SettingsWindow`.
- **Call Graph Cleanup**: Removed `IsAggregateParent` from `ProviderCardPresentation` (was set but never read). Eliminated `TryGetDualQuotaBucketUsedPercentages` — dual bucket percentages are now computed once in `ProviderCardPresentationCatalog.Create` and carried on the presentation record.
- **No Console Window**: Monitor and Web server no longer open a black console window on Windows (`OutputType=WinExe`).
- **Test Cleanup**: Removed fragile source-scanning and private-reflection architecture tests; replaced with negative guards and dynamic file discovery.

## [2.2.28-beta.37] - 2026-03-14

### Fixed
- **Claude Code OAuth Token**: Fixed monitor using stale OAuth access token that expired after ~1 hour. The provider now re-reads `~/.claude/.credentials.json` on every request to pick up tokens refreshed by the Claude Code CLI.
- **Claude Code Fallback Chain**: Skip the useless API rate-limit probe when the token is an OAuth token (always returns 401). Add single retry with 2s delay on OAuth 429 instead of immediately falling through to the CLI.
- **Kimi Auth Key Priority**: Reordered legacy auth file discovery so `~/.local/share/opencode/auth.json` (active, fresh keys) is read last and wins over `~/.opencode/auth.json` (legacy, potentially stale keys). Added debug logging when auth keys get overwritten.

## [2.2.28-beta.36] - 2026-03-14

### Fixed
- **Kimi Provider**: Renamed provider ID from `kimi` to `kimi-for-coding` to match OpenCode's auth.json key format. Added `~/.opencode/auth.json` to config path catalog for key discovery.
- **Monitor Version Mismatch**: The Slim UI now detects when a running monitor has a different version (stale after upgrade) and automatically restarts it, fixing the Claude Code timeout issue after beta updates.

## [2.2.28-beta.35] - 2026-03-14

### Fixed
- **Claude Code Timeout**: Fixed provider timeout caused by Polly retry-on-429 policy consuming ~14s of the 25s budget. Providers now use a PlainClient without retry policies since they manage their own fallback chains.
- **OpenAI Dual Bars**: Fixed dual quota bars not rendering for OpenAI because legacy database records used PascalCase `"WindowKind"` key instead of `"window_kind"`, causing deserialization to silently drop the value.
- **OpenRouterProvider Definition**: Aligned `StaticDefinition` (`isQuotaBased: true`) with runtime behavior, fixing inconsistency between definition and returned usage.
- **ProviderUsageDisplayCatalog**: Fixed hardcoded `parentIsQuota: true` that would invert percentages for non-quota providers.
- **Web Dashboard**: Replaced raw `RequestsPercentage` with `UsedPercent`/`RemainingPercent` in Index, Provider, and History pages, eliminating manual `IsQuotaBased` conditionals.

### Changed
- **Semantic Naming**: Added `UsedPercent`/`RemainingPercent` computed properties to `ProviderUsage`, marked `RequestsPercentage` obsolete. Added `IsStale` to `ProviderUsageDetail`, `RateLimit` to `ProviderUsageDetailType`, marked old `WindowKind` names (`Primary`/`Secondary`/`Spark`) obsolete.
- **Upstream-First Design**: Providers are the single source of truth. `GroupedUsageDisplayAdapter` uses `SetPercentageValue` instead of string regex side-channel. `GroupedUsageProjectionService` prefers typed `TryGetPercentageValue` over `GetEffectiveUsedPercent`.
- **Design Principle (AGENTS.md)**: Documented that provider classes own usage semantics, definitions are immutable per provider ID, and the database stays lean.

### Added
- **HttpClient Regression Tests**: 6 new tests verifying PlainClient registration without Polly policies (827 total tests).

## [2.2.28-beta.34] - 2026-03-14

### Fixed
- **Provider DI Registration**: Fixed HttpClient singleton registration in Monitor DI container, resolving startup errors for ClaudeCodeProvider and KimiProvider that depend on plain `HttpClient` injection.
- **Quota Percentage Calculation**: Fixed OpenAI and Codex providers to use the highest usage percentage across all quota windows (primary, secondary, spark) for the parent display, resolving incorrect 0% usage when primary window was empty but secondary/spark had actual usage.

### Added
- **Provider DI Integration Tests**: Added comprehensive integration tests (`ProviderDiRegistrationTests`) that verify all providers can be resolved from the DI container, preventing future registration issues.

## [2.2.28-beta.33] - 2026-03-14

### Added
- **Claude Code OAuth Integration**: Added OAuth usage endpoint support for Claude subscription users with dual quota buckets (5-hour burst and 7-day rolling window), model-specific breakdowns (Sonnet/Opus), and extra usage status.
- **WPF Attached Behaviors**: Added declarative event handling behaviors (`WindowDragBehavior`, `KeyboardShortcutBehavior`, `CloseWindowBehavior`, `OpenFolderBehavior`) to reduce code-behind.
- **Design-Time ViewModels**: Added design-time data support for XAML designer preview with sample provider cards and sections.
- **Reactive Polling Service**: Added Rx-based polling service with throttling, error recovery, and disposable subscriptions.
- **WeakEventManager**: Added memory-safe event subscription pattern for privacy mode changes.

### Fixed
- **MinimaxProvider Quota Semantics**: Fixed `RequestsPercentage` to store remaining percentage instead of used percentage, aligning with quota-based provider contract.
- **Analyzer Compliance**: Resolved ~280 analyzer warnings across Core, Infrastructure, Monitor, and UI projects (SA1201, SA1202, SA1203, SA1204 member ordering).

### Changed
- **Beta Version Bump**: Advanced shared version metadata and packaging references to `2.2.28-beta.33`.
- **Provider Test Coverage**: Added comprehensive tests for ClaudeCode (16 tests), Kimi (17 tests), and Minimax (22 tests) providers with realistic API response data.

## [2.2.28-beta.32] - 2026-03-14

### Changed
- **Beta Version Bump**: Version bump only (superseded by beta.33).

## [2.2.28-beta.31] - 2026-03-13

### Changed
- **Beta Version Bump**: Advanced shared version metadata and packaging references to `2.2.28-beta.31`.
- **Provider Label Authority**: Split canonical provider labels from dynamic runtime labels so metadata is the source of truth for provider naming across monitor, UI, and CLI flows.
- **Provider Source Label Cleanup**: Aligned Codex, OpenAI, Antigravity, Gemini, Minimax, Mistral, and Z.ai provider-emitted labels with provider metadata, keeping dynamic child/model labels intentional instead of accidental drift.

## [2.2.28-beta.30] - 2026-03-13

### Changed
- **Beta Version Bump**: Advanced shared version metadata and packaging references to `2.2.28-beta.30`.
- **Provider-Defined Display Semantics**: Consolidated used-vs-remaining preference handling behind a shared display-preferences service and provider-driven presentation contracts.
- **Typed Percentage Details**: Added typed percentage semantics for provider detail rows so cards, tooltips, and tray items render consistent used/remaining text across Copilot, Antigravity, Gemini, Codex, Kimi, and OpenAI flows.
- **Contract Guardrails**: Expanded provider metadata and persistence coverage so grouped usage, derived naming, and quota semantics stay aligned with the provider catalog.

## [2.2.28-beta.27] - 2026-03-11

### Changed
- **Beta Version Bump**: Advanced beta release metadata and packaging references to `2.2.28-beta.27`.

## [2.2.28-beta.26] - 2026-03-11

### Changed
- **Strict Catalog Architecture**: Removed legacy provider suppression APIs and consolidated provider visibility/persistence decisions to metadata-driven catalog gates.
- **Monitor Pipeline Simplification**: Removed endpoint-level provider suppression and enforced catalog persistence gates in refresh selection and usage database read/write paths.
- **Settings Metadata Cleanup**: Replaced Minimax and derived-provider UI special-casing with provider metadata (`settingsAdditionalProviderIds`, direct derived-visibility checks).

## [2.2.28-beta.25] - 2026-03-11

### Fixed
- **Auth Discovery Regression**: Restored support for legacy/shared OpenCode auth file locations so provider keys are discovered when present outside `~/.opencode/auth.json`.
- **Provider Key Loading in Beta**: Fixed Synthetic and Z.AI key resolution when keys exist in `~/.local/share/opencode/auth.json`.

### Changed
- **CI Test Stability**: Hardened test workflow defaults for deterministic .NET execution and consolidated test-assembly path resolution to reduce flaky CI failures.

## [2.2.28-beta.24] - 2026-03-11

### Added
- **Gemini CLI Auth Fallback**: Added provider-level fallback from OpenCode Antigravity accounts to native Gemini CLI auth files (`.gemini/oauth_creds.json` + project mapping), with account identity extraction and deterministic project resolution.
- **Provider Auth Contract Guardrails**: Added regression tests that enforce provider-specific auth fallback declarations to prevent discovery drift.

### Changed
- **Provider-Specific Fallback Coverage**: Added missing discovery fallback metadata for DeepSeek, Synthetic, and Z.AI (environment and Roo mappings), plus Gemini environment variable discovery parity.
- **Auth Flow Documentation**: Updated data-flow and environment-variable docs to reflect actual per-provider fallback order and supported sources.

## [2.2.28-beta.23] - 2026-03-11

### Changed
- **Auth Source Precedence**: Added app-local auth (`%LOCALAPPDATA%\\AIUsageTracker\\auth.json`) as a final auth source so app-owned keys are read last and override earlier auth sources.
- **Config Path Deduplication**: Deduplicated config entries when auth paths resolve to the same file to prevent duplicate merges.
- **Auth Flow Documentation**: Added a dedicated auth information flow reference in `docs/auth_information_flow.md`.

### Tests
- **Auth Flow Guardrails**: Added tests that lock config source ordering and verify app-owned auth precedence over earlier auth files.

## [2.2.28-beta.22] - 2026-03-11

### Changed
- **Beta Version Bump**: Updated shared tracker version, README badge, and release packaging scripts for `2.2.28-beta.22`.
- **Release Validation Reliability**: Fixed release script validation to generate appcasts for the correct channel and hardened appcast verification for namespaced Sparkle fields.

## [2.2.28-beta.21] - 2026-03-10

### Changed
- **Beta Version Bump**: Updated shared tracker version, README badge, and release packaging scripts for `2.2.28-beta.21`.

## [2.2.28-beta.20] - 2026-03-08

### Added
- **Burn Rate Forecasting**: Added forecast methods, confidence levels, and trend detection for provider usage projections.
- **OpenCode CLI Detail Capture**: Expanded the OpenCode CLI provider to return richer usage detail data.
- **Web View Coverage**: Added comprehensive Razor Page rendering and model-binding tests, plus a more stable local web test harness.
- **Release Validation Workflow**: Added a stable local test runner and clearer pre-push validation guidance for release prep and CI troubleshooting.

### Fixed
- **Monitor Startup and Contract Checks**: Hardened monitor startup metadata validation, monitor-service state handling, and OpenAPI contract verification.
- **Burn Rate Reliability**: Fixed burn-rate forecast availability/state handling and related test expectations.
- **Web Test Execution**: Fixed web-test artifact path resolution and test assembly fallback handling in CI/local workflows.

### Refactored
- **Analyzer Compliance Sweep**: Applied a broad analyzer cleanup across Core, Infrastructure, Monitor, Web, Tests, and Slim UI, including stricter async, comparer, and collection-abstraction rules.
- **Mechanical Cleanup Pass**: Consolidated monitor service flows, cleaned up web/database abstractions, and simplified screenshot/test helper structure without changing release behavior.

## [2.2.28-beta.19] - 2026-03-06

### Added
- **Architecture Guardrails**: Added provider-id guardrail coverage and direct tests for shared session identity parsing.
- **Safer Local Test Slices**: Added bounded `vstest` slice execution for faster local verification of targeted changes.

### Fixed
- **Provider Discovery Consistency**: Fixed Codex, OpenAI, OpenCode, and Claude session discovery to use provider metadata for auth file paths and schemas.
- **Config Persistence**: Fixed non-persisted derived provider IDs such as `codex.spark` so they are skipped correctly on save.
- **Codex Auth Candidate Search**: Fixed Codex default auth loading so metadata-defined fallback candidates stay enabled instead of being pinned to a single path.

### Refactored
- **Provider Definitions as Source of Truth**: Centralized provider discovery, canonicalization, visibility, visuals, startup refresh, and session-auth ownership behind provider definitions and `ProviderMetadataCatalog`.
- **Slim UI Provider Composition**: Extracted provider settings, status, usage, tooltip, visual, and deterministic fixture behavior into dedicated catalogs/helpers to remove window-level duplication.
- **Shared Session Identity Parsing**: Moved JWT and auth identity parsing into `SessionIdentityHelper` and removed repeated OpenAI/Codex/UI implementations.
- **Dependency and Analyzer Refresh**: Updated compatible dependencies and enabled a stricter analyzer stack while keeping the solution on the .NET 8 baseline.

## [2.2.28-beta.18] - 2026-03-06

### Added
- **Dual Progress Bars**: Added full support for displaying primary and secondary quota progress bars simultaneously for Kimi, OpenAI Codex, and GitHub Copilot.
- **UI Refactoring**: Consolidate provider rendering to improve the layout footprint and fix the dual pass issue in the Slim UI.

### Removed
- **Providers**: Removed Anthropic and OpenAI providers from the codebase.

### Fixed
- **Antigravity Tracking**: Fixed an issue where the local Antigravity server probe would hang on incorrect listening ports by adding an explicit HTTP timeout.

## [2.2.28-beta.10] - 2026-03-05

### Added
- **Raw Snapshot Fields**: All providers now populate RawJson and HttpStatus fields for improved debugging and monitoring
  - Added tests for AnthropicProvider, GeminiProvider, OpenCodeZenProvider to verify field population
  - Added test fixtures for antigravity, github-copilot, and synthetic providers
- **Gemini Provider Improvements**: Enhanced GeminiProvider with path override support for testing and improved error handling

### Removed
- **EvolveMigrationProvider**: Deleted deprecated provider (191 lines)

### Fixed
- **Kimi Provider Dual Progress Bars**: Added `DetermineWindowKind()` method to correctly set `WindowKind.Secondary` for weekly limits (7+ days), enabling dual progress bar display in UI

### CI/CD
- Added Web Tests job to CI pipeline
- Improved Playwright browser installation in CI workflow
- Fixed path handling in GitHub Actions PowerShell scripts

## [2.2.26] - 2026-02-28

### Added
- Dual release channel support (Stable and Beta)
- Update channel selector in Settings window
- develop branch for beta releases

### Changes
- Solution file updated to reference AIUsageTracker.* projects
- App icon now properly embedded in all executables

### CI/CD
- New release.yml workflow with channel parameter
- publish.yml updated to detect beta releases from tag patterns
- generate-appcast.sh script with channel support
- Beta appcast XML files for all architectures

### Application
- UpdateChannel enum in Core (Stable/Beta)
- GitHubUpdateChecker now uses channel-specific appcast URLs

## [2.2.25] - 2026-02-27

### Added
- Pay-as-you-go support for Mistral AI
- Automatic retry for Gemini API on common failures

### Fixed
- Anthropic authentication error handling
- Layout issues on high-DPI displays
