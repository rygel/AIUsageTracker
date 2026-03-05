# Changelog

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
