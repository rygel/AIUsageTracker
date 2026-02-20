# Changelog

## [Unreleased]

## [2.0.2] - 2026-02-20

### Changed
- Windows installer now offers selectable components: Tracker (Slim UI), Classic UI, Agent, Web UI, and CLI.
- Startup and desktop shortcut options now target Tracker (Slim UI) instead of the classic UI executable.

### Fixed
- Publish workflow `create-release` job now checks out repository files before appcast generation, preventing missing script failures.

## [2.0.1] - 2026-02-20

### Changed
- Slim UI now renders Antigravity models as standalone provider cards (no subgroup/sub-model nesting), with explicit `[Antigravity]` labels and Antigravity logo mapping.

### Fixed
- Corrected Antigravity model usage math when quota metadata is missing: affected models now default to `100% remaining` (`0% used`), fixing Gemini 3 / 3.1 Pro displays.

## [2.0.0] - 2026-02-20

### Added
- Native Codex provider support using local auth and ChatGPT `wham/usage` parsing.
- Snapshot-based provider contract tests for Antigravity and Codex parsing stability.
- Shared release scripts for consistency validation and appcast generation.

### Changed
- Slim UI is now the primary desktop UI, rebranded as **AI Consumption Tracker** with title-bar icons and improved tray/settings behavior.
- Antigravity rendering now uses strict payload-defined model names/grouping with grouped model rows.
- Release versioning is centralized in `Directory.Build.props`, with CI dry-run checks for release scripts.
- Added docs architecture links that explicitly reference both the Agent and the Web UI components.

### Fixed
- Agent refresh endpoint and DI wiring issues that blocked Slim UI from receiving live usage data.
- Remaining/used quota percentage semantics aligned across providers, UI display, and alert logic.
- GitHub Copilot quota/reset handling now prefers Copilot internal quota snapshots with monthly reset behavior.

## [1.8.7-alpha.2] - 2026-02-19

### Added
- **Codex Native Provider**: Added local Codex auth detection and native usage parsing from ChatGPT `wham/usage`.
- **Antigravity Detail Metadata**: Added strict `group_name` and `model_name` metadata to provider detail payloads for grouped rendering.

### Changed
- **Slim Antigravity Layout**: Replaced sub-bars with grouped per-model rows and removed the synthetic parent percentage calculation.
- **Slim Window Behavior**: Main close button now hides to tray, and settings now has its own taskbar entry.

### Fixed
- **Slim Agent Lifecycle**: Removed blocking start/stop paths that could hang the UI during restart flows.
- **Settings Theme Consistency**: Dark theme now applies to font selection combo popups and settings history grid styling.

## [1.8.6] - 2026-02-12

### Added
- **System Resume**: Auto-refresh provider data when resuming from sleep/hibernate

### Changed
- **Z.AI**: Use TOKENS_LIMIT reset time when available

## [1.8.5] - 2026-02-11

### Fixed
- **Z.AI Provider**: Handle percentage-only API responses after quota reset
- **Z.AI Provider**: Auto-detect Unix timestamps (seconds vs milliseconds) for reset dates
- **UI Tests**: Fix NullReferenceException when Application.Current is null in headless CI environment

### Added
- **Debug Mode**: Add --debug command line argument with detailed logging for troubleshooting
- **Settings Dialog**: Restore Font Family combo, Font Size input, Bold/Italic checkboxes
- **Settings Dialog**: Restore Yellow/Red thresholds, Invert progress bars, Auto refresh interval

### Changed
- **Theme Support**: Settings dialog now uses theme resource keys instead of hardcoded colors

## [1.8.4] - 2026-02-10

### Fixed
- **Release Workflow**: Fixed bash syntax error with escaped_version variable
  - Removed problematic command substitution that was causing "unexpected EOF"
  - Version numbers don't need escaping in sed replacement strings
  - This should finally allow releases to complete successfully

## [1.8.3] - 2026-02-10

### Fixed
- **Release Workflow**: Fixed bash syntax errors in CI/CD workflows
  - Escaped dots in version numbers for sed commands (release.yml)
  - Added missing closing parenthesis in date command (publish.yml)
  - These fixes allow automated releases to complete successfully

## [1.8.2] - 2026-02-10

### Fixed
- **Release Workflow**: Fixed bash syntax error when updating version files
  - Version numbers containing dots were not properly escaped in sed commands
  - This prevented the release workflow from updating version files correctly

## [1.8.1] - 2026-02-10

### Added
- **Changelog Window**: Added ability to view release notes before updating
  - Fetches changelog from GitHub API
  - Shows in scrollable window with dark theme
  - Accessible from update notification banner
- **Architecture-Specific Updates**: Fixed update system to support x64, x86, and arm64 architectures
  - Each architecture now has its own appcast file
  - Application automatically downloads correct installer for current architecture

### Fixed
- **Update System**: Fixed architecture detection and installer naming
  - Installers now include architecture suffix (e.g., `_x64.exe`)
  - Prevents x64/x86/arm64 installer conflicts
- **Appcast Generation**: Now generates separate appcast files per architecture
  - appcast_x64.xml, appcast_x86.xml, appcast_arm64.xml

## [1.8.0] - 2026-02-10

### Added
- **Claude Code Provider**: Complete implementation with Anthropic API integration
  - Detects API key from environment variables and credentials file
  - Fetches real-time rate limit information from API headers
  - Shows usage percentage and tier information
  - Displays warnings when approaching rate limits (70% and 90% thresholds)
  - Detailed tooltips with rate limit breakdown
  - Falls back to CLI if API is unavailable
- **Cloud Code Removal**: Removed redundant Cloud Code provider
  - Consolidated to single Claude Code provider
- **Z.ai Coding Plan Display Name**: Updated settings dialog to show "Z.ai Coding Plan"

### Fixed
- **Window Focus**: Fixed "Top" setting not being reapplied when window is re-shown
  - Window now correctly stays on top of other windows after being hidden and reshown
  - Ensures Topmost property is reapplied when clicking tray icon or notification
- **Claude Code API Integration**: Fixed non-existent usage API endpoint
  - Now uses rate limit headers from API responses
  - Provides accurate tier and usage information

## [1.7.19] - 2026-02-10

### Fixed
- **Z.AI Provider**: Fixed reset time parsing from Unix timestamp (milliseconds)
- **Antigravity Provider**: Fixed PaymentType classification in all error cases
  - All return paths now correctly set `PaymentType = Quota`
  - Provider now appears in correct "Plans & Quotas" section when not running

### Added
- **Collapsible Sections**: Added collapsible groups for "Plans & Quotas" and "Pay As You Go"
- **File Logging**: Added file logging to UI for debugging
- **Unit Tests**: Added AntigravityProviderTests to verify PaymentType consistency

## [1.7.18] - 2026-02-10

### Fixed
- **Automatic Updates**: Fixed appcast.xml filename mismatch
  - Corrected installer filename in update manifest
  - Updates now download and install correctly
- **Update Check Interval**: Reduced from 2 hours to 15 minutes

## [1.7.17] - 2026-02-10

### Fixed
- **Progress Bar Colors**: Fixed color display for quota-based providers
  - Bars now correctly show green when plenty of quota remains, yellow when running low, red when nearly depleted
  - Affected providers: Synthetic, Z.AI, Antigravity, GitHub Copilot

## [1.7.16] - 2026-02-09

### Fixed
- **Quota Bar Calculation**: Fixed progress bar to show remaining percentage for quota-based providers
  - Shows 100% (full bar) when all credits are available
  - Shows 0% (empty bar) when quota is exhausted
  - Different calculation for quota vs credits-based providers
- **Synthetic Provider**: Fixed reset time display to always show regardless of date

### Added
- **Automatic Updates**: Implemented automatic download and installation of updates
  - Shows progress dialog during download
  - Automatically runs installer and restarts application
  - Falls back to browser if download fails
- **Developer Documentation**: Added progress bar calculation guidelines to AGENTS.md

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
