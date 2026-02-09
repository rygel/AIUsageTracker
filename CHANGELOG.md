# Changelog

## [1.7.15] - 2026-02-09

### Added
- Windows 11 native notifications with per-provider and global controls
- Support for Kilo Code, Roo Code, and Claude Code as API key discovery sources

### Changed
- Notifications disabled by default
- Kilo Code removed as standalone provider (discovery source only)
- Test providers removed from settings

### Fixed
- Version number display in screenshots

## [1.7.14] - 2026-02-09

### Fixed
- **NetSparkle Integration**: Properly integrated NetSparkleUpdater for automatic update checking
  - Added automatic appcast.xml generation to release workflow
  - Rewrote GitHubUpdateChecker to use NetSparkle's SparkleUpdater class
  - Fixed type conflicts and simplified MainWindow update handling
  - Removed dependency on non-existent GitHubReleaseAppCast class

## [1.7.13] - 2026-02-09

### Added
- **Privacy Mode in Settings**: Added privacy toggle button to Settings window, synchronized with main window
- **Quick Close**: Added X button to Settings window header for quick closing

### Fixed
- **Progress State Updates**: Progress bars now update in-place when toggling between error/missing and progress display, eliminating UI flicker
- **Version Synchronization**: Updated AssemblyVersion and FileVersion to match Version tag in all project files and release workflow

## [1.7.12] - 2026-02-08

### Added
- **Smooth In-Place Updates**: Drastic reduction in UI flickering during polling by mutating existing UI elements instead of recreating them.
- **Part-Level Control**: Fine-grained control over progress bars and text elements for smoother visual transitions.

### Fixed
- **UI Update Logic**: Resolved an issue where child bars (detailed usage) could become orphaned or stale during updates.
- **UI Test Stability**: Fixed the `UpdateProviderBar_ShouldReplaceExistingProviderBar` unit test and standardized `Tag` assignments.

## [1.7.11] - 2026-02-08

### Fixed
- **Release Workflow**: Synchronized all version strings and improved automated distribution scripts.
- **Inno Setup**: Corrected Pascal script syntax for reliable installer generation.

## [1.7.10] - 2026-02-08

### Fixed
- **Inno Setup**: Fixed syntax errors in Pascal scripts and improved architecture validation logic
- **CI Build**: Fixed Inno Setup /Q flag causing build failures in GitHub Actions
- **Publish Script**: Added robust installer detection and ensured the workflow fails if Inno Setup is missing or fails
- **Compiler Warnings**: Resolved all compiler warnings for clean builds

## [1.7.9] - 2026-02-08

## [1.7.9] - 2026-02-08

### Fixed
- **CI Build**: Resolved compiler warnings blocking GitHub Actions publish workflow
- Fixed unused variable warning in MainWindow.xaml.cs
- Fixed nullable value type warning in AntigravityProvider.cs

## [1.7.8] - 2026-02-08

## [1.7.8] - 2026-02-08

### Added
- **Antigravity Caching**: Caches usage data when Antigravity is running successfully
- **Offline Display**: Shows cached data with "Last refreshed: Xm ago" when Antigravity is not running
- **Reset Information**: Displays "Resets in Xh Ym" when reset times are available in cached data
- **Provider Visibility**: Antigravity provider bar now always shows (even when not running)

### Changed
- **Documentation**: Removed deprecated "Business Logic Rules" section from AGENTS.md

## [1.7.7] - 2026-02-08

### Fixed
- **UI Fixes**:
  - Fixed flickering during refresh by removing `Clear()` call from `RenderUsages()`
  - Added filtering logic to `UpdateProviderBar()` to hide unused providers during auto-refresh
  - Providers are now filtered consistently during both full and incremental refreshes
- **Test Coverage**:
  - Added `UpdateProviderBarTests.cs` with 5 test cases covering provider filtering scenarios
  - All tests verify proper filtering behavior when "Show All" is enabled/disabled

## [1.7.6] - 2026-02-08

### Added
- **Check for Updates Button**: Added manual check for updates button in Settings dialog footer for easier update checking from settings UI
- **Auto-Download**: Update installer now downloads automatically instead of opening browser with progress dialog
- **Architecture Detection**: Automatically detects user architecture (x86, x64, arm64) and downloads correct installer
- **Installer Validation**: Inno Setup now validates architecture matches system before installing

### Fixed
- **Architecture Bug**: Fixed issue where wrong architecture (ARM) was downloaded instead of x64 for x64 systems
- **32-bit Detection**: Properly distinguishes 32-bit (x86) from 64-bit (x64) Windows processes
- **Download Flow**: Improved UX from browser-based to one-click download and install

## [1.7.5] - 2026-02-08

### Added
- **Automated Release Workflow**: GitHub Actions workflow that automatically updates version files, validates changes, creates git tags, and generates GitHub releases
- **Code Quality Tooling**: Added `.editorconfig` with Roslyn analyzer rules for consistent code style across the project
- **CI Formatting Verification**: Added `dotnet format --verify` step to CI workflow to ensure code formatting compliance
- **Developer Documentation**: Comprehensive "Developer Resources" section in user_manual.md covering code quality, local development, CI/CD workflows, and best practices

### Changed
- **Performance Optimizations**:
  - Config caching: 5-second in-memory cache reduces file I/O from 3x to 1x per refresh
  - HTTP request throttling: Limits to 6 concurrent connections to prevent network congestion
  - Incremental UI updates: Provider bars appear as data arrives instead of waiting for all providers

### Fixed
- **UI Fixes**:
  - Fixed bug where PrivacyChanged event didn't call RenderUsages to update UI display
  - Removed duplicate privacy checkbox from Settings dialog (Privacy button only in title bar)
  - Privacy mode now only accessible via title bar button
- **Code Quality**:
  - Refined analyzer rules in `.editorconfig` to avoid false positives for WPF-specific patterns
  - Changed CA1307 severity from error to suggestion for missing StringComparison in .Equals() calls

## [1.7.4] - 2026-02-08
### Added
- **Update Checks**: automatically notifies of new GitHub releases on startup and every 2 hours.
- **Auto Refresh Interval**: Introduced a configurable background refresh timer for API consumption data (default: 5 minutes).
- **Settings UI**: Added "Auto Refresh (Minutes)" input to the Layout tab.
- **Documentation Enhancements**: Comprehensive screenshots section with Dashboard, Settings, Info Dialog views

## [1.7.1] - 2026-02-08
### Added
- **Windows ARM Support**: Restored support for Windows ARM64 and ARM (32-bit).

## [1.7.0] - 2026-02-08
### Added
- **DeepSeek Improvements**: Added official SVG logo, detailed multi-currency balance tracking (CNY/USD), and standardized as pay-as-you-go.
- **Capacity Bars by Default**: "Invert Progress Bars" is now enabled by default for a "Health Bar" (Full = Unused) experience.
- **Fixed Color Inversion**: Standardized color logic so "Danger" colors (Red) correctly map to high usage regardless of bar inversion.
- **CI Hardening**: Resolved all 9 build warnings and standardized nullability handling.

### Removed
- **SimulatedProvider**: Removed the test provider from the UI and CLI.

## [1.6.0] - 2026-02-07
### Added
- **New Provider Support**:
  - **Xiaomi**: Added dedicated support for Xiaomi Cloud / MiMo API monitoring (Balance & Quota).
  - **Minimax**: Added robust support for both International (`minimax-io`) and China (`minimax`) endpoints.
  - **Kimi (Moonshot AI)**: Added support for checking usage and limits.
- **UI Enhancements**:
  - Added official/branded icons for Xiaomi, Kimi, and Minimax.
  - Providers in Settings are now propertly mapped with distinct icons.
  - Updated tray icon support for new providers.

## [1.5.3] - 2026-02-07
### Added
- OpenAI JWT token support and improved authentication discovery.
- Environment variable discovery for all providers.

## [1.5.2] - 2026-02-06
### Added
- Compact Providers tab layout in Settings dialog, reducing vertical space per provider by ~40-50%.
### Fixed
- Corrected provider icon mappings for Google services (Gemini CLI, Antigravity, Cloud Code) and Anthropic.
### Changed
- Increased Settings dialog default height from 450px to 550px for better visibility.
- Moved "Tray" checkbox to provider header row for more compact layout.
- Removed "API Key" label to maximize input field width.
- Reduced card padding, margins, icon size, and font sizes throughout Providers tab.

## [1.5.1] - 2026-02-06
### Added
- Boxy UI aesthetics: Windows-style sharp corners for buttons, input fields, and windows.
- Proper application icon support (.ico) for Windows Start menu and Taskbar.
### Fixed
- Fixed missing installer icon in Inno Setup scripts.
- Improved Info Dialog: Centered on screen, added title-bar dragging, and enabled auto-sizing to remove scrollbars.
- Refined Info Dialog content: Simplified title to "Info", removed logo/credits, and moved version display.
- Fixed scrollbar clipping issue with custom dark theme.
### Changed
- Refined scrollbar visibility: Scrollbars in the main dashboard and settings now only appear when content overflows.
- Themed scrollbars to match dark mode aesthetics globally.
- Replaced "Current Directory" in Info Dialog with a clickable, wrapping link to the configuration file (`auth.json`).

## [1.5.0] - 2026-02-06
### Added
- Headless UI testing infrastructure for WPF using `WpfFact` and `ServiceCollection`.
- Dedicated unit tests for `PrivacyHelper` masking logic.

### Changed
- Refined Privacy Mode: Implemented surgical masking that only affects sensitive parts (emails, usernames) within descriptions while preserving surrounding context.
- Hardened UI rendering to be more robust in headless/test environments.

## [1.4.0] - 2026-02-06)

- **GitHub Copilot Integration**:
  - Implemented OAuth Device Flow authentication for GitHub
  - Added `GitHubAuthService` for secure token management and persistence
  - Created `GitHubLoginDialog` for user-friendly authentication flow
  - Integrated Copilot usage tracking with API rate limit visualization
  - Display quota as progress bar in "Plans & Quotas" section with color-coded thresholds
  - Show plan type (Individual/Business/Enterprise) and account name
  - Labeled API usage as "API Rate Limit (Hourly)" to distinguish from monthly billing quotas
  - Moved GitHub authentication UI to Providers tab for better organization
  - GitHub Copilot card now always visible in provider list
  - Added `CodexProvider` for Codex model tracking

- **UI/UX Improvements**:
  - Updated "Scan for Keys" button to also refresh usage data from all providers
  - Integrated login/logout functionality directly in GitHub Copilot provider card
  - Improved authentication status display with color-coded indicators

- **Font Settings Improvements**:
  - Implemented System Font Selection: Users can now choose from all installed fonts.
  - Live Font Preview: Font settings changes now update a preview box in real-time.
  - Reset to Default: Added a button to restore default font settings (Segoe UI, 12pt).
  - Improved Propagation: Font settings now apply immediately to the main window upon closing Settings.
  - Centralized Settings Logic: Standardized how settings are shown and refreshed.

## v1.3.3 (2026-02-06)
- Fixed "white on white" font visibility issue using a custom ControlTemplate for ComboBox.

## v1.3.2 (2026-02-06)
- Fixed font family visibility in Settings (white text on white background).
- Fixed various UI build and syntax errors.
- Disabled Winget submission temporarily.

## v1.3.1 (2026-02-06)
- Fixed cross-platform release asset generation (Linux/macOS).
- Updated installer to support architecture-specific builds (x64/x86).
- Version propagation improvements in CI/CD pipeline.

## v1.3.0 (2026-02-06)
- Modernized UI with icon-based footer.
- Added headless UI testing infrastructure.
- Introduced mock providers for testing.
