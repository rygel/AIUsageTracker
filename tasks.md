# Architecture Improvement Tasks

Generated from code analysis on 2026-03-03.
Updated with additional tasks on 2026-03-03.

---

## P0: Critical (Fix Immediately)

### Async/Threading Issues

- [x] **Fix blocking async in MainWindow constructor**
  - File: `AIUsageTracker.UI.Slim\MainWindow.xaml.cs:74`
  - Problem: `_preferences = UiPreferencesStore.LoadAsync().GetAwaiter().GetResult()` can deadlock UI thread
  - Fix: Initialize with defaults in constructor, load async in Loaded handler
  - Status: **COMPLETED** - Moved async loading to `MainWindow_Loaded` event handler
  - Test: Existing `AppStartupTests.LoadPreferencesAsync_DoesNotBlockThread` covers the store behavior

- [x] **Fix fire-and-forget tasks without tracking**
  - File: `AIUsageTracker.Monitor\Services\ProviderRefreshService.cs:64-97`
  - Problem: Background tasks not awaited on shutdown, potential data loss
  - Fix: Track tasks and await them during shutdown, or use `System.Threading.Channels`
  - Status: **COMPLETED** - Added `_startupTasks` list and `StopAsync` override to await tasks
  - Test: Added `StopAsync_WaitsForStartupTasks` in `MonitorResilienceTests.cs`

- [x] **Add cancellation token to TriggerRefreshAsync**
  - File: `AIUsageTracker.Monitor\Services\ProviderRefreshService.cs:164-286`
  - Problem: Cannot cancel long-running refresh operations
  - Fix: Add `CancellationToken cancellationToken = default` parameter
  - Status: **COMPLETED** - Added cancellation token parameter and checks throughout method
  - Test: Added `TriggerRefreshAsync_AcceptsCancellationTokenParameter` in `StartupAntiHammerTests.cs`

---

## P1: High Priority

### Error Handling Standardization

- [x] **Fix ZaiProvider exception throwing**
  - File: `AIUsageTracker.Infrastructure\Providers\ZaiProvider.cs:40,55`
  - Problem: Throws exceptions instead of returning `ProviderUsage` with `IsAvailable=false`
  - Fix: Return error state objects consistent with other providers
  - Status: **COMPLETED** - Returns ProviderUsage with IsAvailable=false instead of throwing exceptions

- [x] **Add logging to silent catch blocks**
  - File: `AIUsageTracker.Infrastructure\Providers\GitHubCopilotProvider.cs:152-155,226-229`
  - Problem: Empty catch blocks make debugging impossible
  - Fix: Add `_logger.LogDebug()` calls to catch blocks
  - Status: **COMPLETED** - Added debug logging to all catch blocks

- [x] **Standardize IsAvailable semantics**
  - Files: `DeepSeekProvider.cs`, all providers
  - Problem: Inconsistent IsAvailable semantics across providers
  - Fix: Document semantics in ProviderUsage, update DeepSeek to only set IsAvailable=false on auth errors
  - Status: **COMPLETED** - Added XML documentation, DeepSeek uses auth error check

- [ ] **Add logging to silent catch blocks**
  - File: `AIUsageTracker.Infrastructure\Providers\GitHubCopilotProvider.cs:152-155,226-229`
  - Problem: Empty catch blocks make debugging impossible
  - Fix: Add `_logger.LogDebug()` calls

- [ ] **Standardize IsAvailable semantics across providers**
  - Files: `DeepSeekProvider.cs`, `OpenCodeProvider.cs`, `OpenRouterProvider.cs`
  - Problem: Inconsistent meaning of `IsAvailable` on API errors
  - Fix: Document and enforce: `true` = valid key/soft failure, `false` = auth failure/unavailable

### Code Duplication - Shared Models

- [x] **Move ResetEvent to Core**
  - Source: `AIUsageTracker.Monitor\Services\UsageDatabase.cs:512-521`
  - Source: `AIUsageTracker.Web\Services\WebDatabaseService.cs:1106-1115`
  - Description: Identical `ResetEvent` class defined in both projects
  - Fix: Create `AIUsageTracker.Core\Models\ResetEvent.cs`, remove duplicates
  - Status: **COMPLETED** - Created Core.Models.ResetEvent and removed duplicates from Monitor and Web

- [x] **Move Web DTOs to proper location**
  - File: `AIUsageTracker.Web\Services\WebDatabaseService.cs:1094-1120`
  - Problem: `ProviderInfo`, `UsageSummary`, `ChartDataPoint` defined in service file
  - Fix: Move to `AIUsageTracker.Web\Models\WebModels.cs`
  - Status: **COMPLETED** - Created Web.Models namespace and moved DTOs
  - Status: **COMPLETED** - Created Web.Models namespace and moved DTOs

- [ ] **Move Web DTOs to proper location**
  - File: `AIUsageTracker.Web\Services\WebDatabaseService.cs:1094-1131`
  - Problem: `ProviderInfo`, `UsageSummary`, `ChartDataPoint` defined in service file
  - Fix: Move to `AIUsageTracker.Core\Models\` or `AIUsageTracker.Web\Models\`

### Missing Abstractions

- [x] **Create ProviderBase class with shared helpers**
  - Files: All providers in `AIUsageTracker.Infrastructure\Providers\`
  - Problem: Every provider independently creates error objects, HTTP requests, reset time parsing
  - Fix: Create abstract `ProviderBase` class with:
    - `CreateUnavailableUsage(string description, int httpStatus = 503)`
    - `CreateAuthorizedRequest(string url, string apiKey)`
    - `FormatResetTime(DateTime? resetTime)`
  - Status: **COMPLETED** - Created ProviderBase.cs with shared helpers for providers

- [ ] **Create IAuthFileLocator service**
  - Source: `CodexProvider.cs:432-460`
  - Source: `CodexAuthService.cs:87-115`
  - Problem: Identical `GetAuthFileCandidates()` methods in both files
  - Fix: Extract to shared service in Infrastructure

---

## P2: Medium Priority

### Code Duplication - Utilities

- [x] **Create AppPaths utility class**
  - Source: `AIUsageTracker.Monitor\Program.cs`
  - Source: `AIUsageTracker.Monitor\Services\UsageDatabase.cs`
  - Source: `AIUsageTracker.Web\Services\WebDatabaseService.cs`
  - Problem: Directory path resolution duplicated 3 times
  - Fix: Create `AIUsageTracker.Core\Utilities\AppPaths.cs` for current directory logic only
  - Status: **COMPLETED** - Created centralized AppPaths class with GetAppDataDirectory, GetAppDirectory, GetDatabasePath, etc.

- [x] **Create JsonHelpers utility class**
  - Source: `SyntheticProvider.cs:243-319`
  - Source: `CodexProvider.cs:799-855`
  - Problem: Similar JSON parsing utilities (`TryGetDoubleCandidate`, `ReadString`, etc.)
  - Fix: Create `AIUsageTracker.Infrastructure\Utilities\JsonHelpers.cs` with extension methods
  - Status: **COMPLETED** - Created JsonHelpers with ReadString, ReadDouble, ReadBool, TryGetPropertyIgnoreCase, TryGetDoubleProperty, TryGetDoubleCandidate

- [x] **Consolidate config normalization logic**
  - Source: `AIUsageTracker.Monitor\Services\ConfigService.cs:149-184`
  - Source: `AIUsageTracker.Infrastructure\Configuration\JsonConfigLoader.cs:164-202`
  - Problem: `NormalizeOpenAiCodexSessionOverlap` duplicated in both files
  - Fix: Create `AIUsageTracker.Infrastructure\Configuration\ConfigNormalization.cs` with shared methods
  - Status: **COMPLETED** - Created ConfigNormalization with NormalizeOpenAiCodexSessionOverlap and NormalizeCodexSparkConfiguration

### Interface Segregation

- [ ] **Split IConfigLoader interface**
  - File: `AIUsageTracker.Core\Interfaces\IConfigLoader.cs:5-11`
  - Problem: Mixes provider config + preferences concerns
  - Fix: Split into `IProviderConfigLoader` and `IPreferencesLoader`

- [ ] **Split IGitHubAuthService interface**
  - File: `AIUsageTracker.Core\Interfaces\IGitHubAuthService.cs:5-46`
  - Problem: Combines OAuth flow + token management + user profile
  - Fix: Split into `IGitHubDeviceFlow`, `IGitHubTokenManager`, `IGitUserProfile`

- [ ] **Move model classes out of Interfaces namespace**
  - File: `AIUsageTracker.Core\Interfaces\IUpdateCheckerService.cs:9-16` - `UpdateInfo`
  - File: `AIUsageTracker.Core\Interfaces\INotificationService.cs:13-17` - `NotificationClickedEventArgs`
  - Fix: Move to `AIUsageTracker.Core\Models\`

### Logging Improvements

- [ ] **Fix NullLogger usage in ConfigService**
  - File: `AIUsageTracker.Monitor\Services\ConfigService.cs:17-20`
  - Problem: Creates `NullLogger` instances for dependencies
  - Fix: Inject `ILoggerFactory` or use proper DI

- [ ] **Replace static debug mode flag**
  - File: `AIUsageTracker.Monitor\Services\ProviderRefreshService.cs:23,33-36`
  - Problem: Static `_debugMode` not thread-safe, uses mutable static state
  - Fix: Use instance property injected via configuration

---

## P3: Lower Priority

### MVVM Refactoring

- [ ] **Extract MainViewModel from MainWindow code-behind**
  - File: `AIUsageTracker.UI.Slim\MainWindow.xaml.cs` (788 lines)
  - Problem: Business logic, data access, UI creation mixed in code-behind
  - Fix: Create `MainViewModel` with `ObservableCollection<ProviderViewModel>`

- [ ] **Extract SettingsViewModel from SettingsWindow code-behind**
  - File: `AIUsageTracker.UI.Slim\SettingsWindow.xaml.cs` (2420 lines)
  - Problem: Massive code-behind with API calls, file I/O
  - Fix: Create sub-ViewModels for each tab, use MVVM commands

- [ ] **Move theme definitions to XAML ResourceDictionaries**
  - File: `AIUsageTracker.UI.Slim\App.xaml.cs:70-543`
  - Problem: 470+ lines of hardcoded color assignments in C#
  - Fix: Create `DarkTheme.xaml`, `LightTheme.xaml`, etc.

### UI Code Duplication

- [ ] **Create shared ProviderIconFactory**
  - Source: `MainWindow.xaml.cs:615-670`
  - Source: `SettingsWindow.xaml.cs:1425-1484`
  - Problem: Identical `CreateProviderIcon()` and `GetFallbackIconData()` methods
  - Fix: Create singleton `ProviderIconFactory` class
  - Note: Provider cards themselves should remain separate - MainWindow (read-only status) vs SettingsWindow (interactive editing)

- [ ] **Create AccountMasker utility**
  - Source: `MainWindow.xaml.cs:674-686`
  - Source: `SettingsWindow.xaml.cs:1486-1532`
  - Problem: Account masking logic duplicated
  - Fix: Create `AIUsageTracker.Core\Utilities\AccountMasker.cs`

- [x] **Unify progress bar color thresholds**
  - Source: `MainWindow.xaml.cs:608-613` (90% red, 70% yellow)
  - Source: `Web\Pages\Index.cshtml:419-429` (90% high, 50% medium)
  - Problem: Inconsistent thresholds between UIs
  - Fix: Extract to shared `ProgressColorCalculator` in Core
  - Status: **COMPLETED** - Created ProgressColorCalculator with YellowThreshold=70, RedThreshold=90 constants

### Hardcoded Values

- [ ] **Move timer intervals to configuration**
  - Files: `MainWindow.xaml.cs:50`, `SettingsWindow.xaml.cs:52`
  - Fix: Add to `AppPreferences` as configurable properties

- [ ] **Fix hardcoded Monitor port reference**
  - File: `MainWindow.xaml.cs:772`
  - Problem: `http://localhost:5000` hardcoded
  - Fix: Get port from MonitorService configuration

### Provider DI Registration

- [ ] **Register providers in DI container**
  - File: `AIUsageTracker.Monitor\Services\ProviderRefreshService.cs:122-161`
  - Problem: Providers manually instantiated in `InitializeProviders()`
  - Fix: Register all providers in DI, inject `IEnumerable<IProviderService>`

### CancellationToken Standardization

- [ ] **Add CancellationToken to all providers**
  - Files: Various providers in `AIUsageTracker.Infrastructure\Providers\`
  - Problem: Some providers use cancellation, others don't
  - Fix: Standardize all providers to accept optional `CancellationToken`

---

## Summary

| Priority | Count | Focus |
|----------|-------|-------|
| P0 | 3 | Async/deadlock issues |
| P1 | 8 | Error handling, shared models, abstractions |
| P2 | 9 | Duplication, interfaces, logging |
| P3 | 9 | MVVM, UI consolidation, config |

**Total: 29 tasks**

---

## Notes

- **Provider Cards**: MainWindow and SettingsWindow provider cards serve different purposes (read-only status display vs interactive editing) and should remain separate implementations
- **Shared Utilities**: Icon creation, progress bar colors, and account masking can still be consolidated
