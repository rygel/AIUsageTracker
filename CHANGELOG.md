# Changelog

## [Unreleased]

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
