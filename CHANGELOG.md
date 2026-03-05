# Changelog

## [2.2.28-beta.15] - 2026-03-05

### Added
- **Architectural Cleanup**: Significantly simplified and decoupled the UI layer by extracting theme, screenshot, and tray icon logic from `App.xaml.cs` into dedicated services within `AIUsageTracker.UI.Slim/Services`.
- **Centralized Path Management**: Enhanced `IAppPathProvider` to include methods for retrieving Monitor discovery paths and configuration roots, eliminating remaining hardcoded special folder lookups.
- **Improved Dual Path Detection**: Refined UI logic to robustly detect and render dual progress bars for OpenAI, Kimi, and GitHub Copilot by supporting flexible matching of primary, secondary, and spark quota windows.

### Fixed
- **App Orchestration**: Refactored `App` class to be a pure orchestrator using standard dependency injection for all core services, including mock implementations for the Slim UI.
- **Web UI Data Parity**: Restored detailed provider data mapping in the Web dashboard, ensuring dual bars and quota details are correctly displayed.

## [2.2.28-beta.14] - 2026-03-05

### Added
- **GitHub Copilot Dual Path Support**: Implemented dual-quota tracking for GitHub Copilot, allowing simultaneous monitoring of Premium Interactions and short-term quota windows.

## [2.2.28-beta.11] - 2026-03-05

### Added
- **Centralized Path Provider**: Introduced `IAppPathProvider` to manage all file system paths (DB, logs, configs) in one place, improving testability and consistency.
- **Dependency Injection for WPF**: Fully refactored Slim UI to use `Microsoft.Extensions.Hosting` and dependency injection for all windows and ViewModels.
- **Decomposed Database Services**: Refactored `WebDatabaseService` into specialized `IUsageAnalyticsService` and `IDataExportService` for better maintainability.
- **New Models**: Added `ResetEvent`, `ProviderInfo`, `UsageSummary`, and `ChartDataPoint` to `Core.Models` for better cross-project data sharing.

### Fixed
- **Screenshot Mode**: Restored headless screenshot capture mode and command-line argument parsing broken during refactoring.
- **Tray Icon Logic**: Restored tray icon initialization and update logic in the refactored Slim UI.
- **Theme Manifest**: Fixed CI validation by updating theme manifest dates and restoring valid JSON formatting.
- **Database Seeder**: Updated test database seeder to include missing `response_latency_ms` column.
- **Security**: Upgraded caching and logging abstractions to resolve high-severity vulnerabilities.

### CI/CD
- Fixed timeout in Slim Screenshot Baseline by ensuring application exits correctly in headless mode.
- Improved theme validation workflow with better contract parity checks.

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
