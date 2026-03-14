# Changelog

## [Unreleased]

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
