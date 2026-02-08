# AI Consumption Tracker - Agent Guidelines

This document provides essential information for agentic coding assistants working on this .NET 8.0 WPF application.

## Development Workflow

- **Never push directly to `main`**: All changes, including release preparations, MUST be done on a feature branch (e.g., `feature/branch-name`) and integrated via a Pull Request.
- **Atomic Commits**: Keep commits focused and logically grouped.
- **CI/CD Compliance**: Ensure that any UI changes or tests are compatible with the headless CI environment.
- **No Icons in PRs**: When creating pull requests, do not use emojis or icons in the title or body.

## Project Structure

- **AIConsumptionTracker.Core**: Domain models, interfaces, and business logic (PCL)
- **AIConsumptionTracker.Infrastructure**: External providers, data access, configuration
- **AIConsumptionTracker.UI**: WPF desktop application (Windows-only)
- **AIConsumptionTracker.CLI**: Console interface (cross-platform)
- **AIConsumptionTracker.Tests**: xUnit unit tests with Moq mocking
- **AIConsumptionTracker.UI.Tests**: WPF-specific tests

## Build & Test Commands

### Building
```bash
# Build entire solution
dotnet build AIConsumptionTracker.slnx --configuration Debug

# Build specific project
dotnet build AIConsumptionTracker.UI/AIConsumptionTracker.UI.csproj

# Restore dependencies
dotnet restore
```

### Testing
```bash
# Run all unit tests
dotnet test AIConsumptionTracker.Tests/AIConsumptionTracker.Tests.csproj --configuration Debug

# Run UI tests
dotnet test AIConsumptionTracker.UI.Tests/AIConsumptionTracker.UI.Tests.csproj --configuration Debug

# Run all tests (no rebuild)
dotnet test --no-build --verbosity normal

# Run a single test
dotnet test --filter "FullyQualifiedName~GetAllUsageAsync_LoadsConfigAndFetchesUsageFromMocks"

# Run tests by class
dotnet test --filter "FullyQualifiedName~ProviderManagerTests"
```

### Automated Screenshots
To generate updated screenshots for documentation (headless and in Privacy Mode):
```bash
# Run from the UI bin directory or project root
AIConsumptionTracker.UI.exe --test --screenshot
```
> [!NOTE]
> The `--test` flag enables explicit UI initialization required for headless rendering. This logic is gated to avoid performance overhead for normal users.

### Publishing
```bash
# Publish Windows UI
.\scripts\publish-app.ps1 -Runtime win-x64

# Publish Linux CLI
.\scripts\publish-app.ps1 -Runtime linux-x64
```

## Code Style Guidelines

### Imports & Namespaces
- Use **file-scoped namespace declarations**: `namespace AIConsumptionTracker.Core.Models;`
- Place using statements at the top, before namespace declaration
- Group by: System → Third-party → Project references (separated by blank lines)
- Explicitly type `using Microsoft.Extensions.Logging;` when needed to avoid ambiguity

### Naming Conventions
- **Classes**: PascalCase (e.g., `ProviderManager`, `AppPreferences`)
- **Methods**: PascalCase (e.g., `GetUsageAsync`, `LoadConfigAsync`)
- **Properties**: PascalCase (e.g., `ProviderId`, `IsAvailable`)
- **Private fields**: _camelCase with underscore prefix (e.g., `_httpClient`, `_logger`)
- **Interfaces**: I prefix (e.g., `IProviderService`, `IConfigLoader`)
- **Async methods**: End with `Async` suffix
- **Boolean properties**: Prefer affirmative phrasing (e.g., `IsAvailable`, `StayOpen`)

### Type & Nullable Guidelines
- **Nullable reference types are enabled globally** - always handle potential nulls
- Use nullable annotations: `public string? ApiKey { get; set; }`
- Prefer non-nullable where possible: `public bool ShowAll { get; set; } = false;`
- Use default value initializers for properties: `public double WindowWidth { get; set; } = 420;`
- Implicit usings enabled - don't add redundant using statements

### Formatting
- **Indentation**: 4 spaces (no tabs)
- **Braces**: Allman style (opening brace on new line)
- **Line length**: Aim for ~120 characters, max 150
- **Blank lines**: One between methods, two between logical sections
- **Object initializers**: Preferred for new objects: `new ProviderUsage { ProviderId = "openai" }`
- **String interpolation**: Use `$"Value: {value}"` over concatenation

### Error Handling
- **ArgumentException**: For missing/invalid parameters with descriptive message
- **Return error state in objects**: For provider errors, return `ProviderUsage` with `IsAvailable = false`
- **Log exceptions**: Use `_logger.LogError(ex, "message")` for unexpected errors
- **Swallow specific exceptions**: Only when appropriate with logging
- **Never throw in async void**: Use `Task` or `async Task` instead

### Async/Await Patterns
- Always `await` async calls (avoid `.Result` or `.Wait()`)
- Use `ConfigureAwait(false)` in library code (non-UI)
- Pass `CancellationToken` when available for long-running operations
- Use `SemaphoreSlim` for async locking
- Return `IEnumerable` for lazy evaluation, `IList` for materialized collections

### Dependency Injection
- Constructor injection only (no property injection)
- All dependencies declared as `readonly` fields
- Register services in `App.xaml.cs` with `Microsoft.Extensions.DependencyInjection`
- Use `ILogger<T>` for logging (never use `Console.WriteLine`)

### Testing Guidelines
- **Arrange-Act-Assert** pattern for all tests
- Use `[Fact]` for normal tests, `[Theory]` with `[InlineData]` for parameterized tests
- Mock interfaces with Moq: `var mock = new Mock<ILogger<ProviderManager>>();`
- Use descriptive test names: `GetAllUsageAsync_LoadsConfigAndFetchesUsageFromMocks`
- Test both success and failure paths
- Avoid implementation details - test behavior

### WPF-Specific Guidelines
- **XAML**: 4-space indentation, self-closing tags when no content
- **MVVM preferred**: Keep code-behind minimal, use bindings
- **Styles**: Define in Window.Resources, use `x:Key` for named styles
- **Colors**: Use hex codes for dark theme (e.g., `#1E1E1E` background)
- **Resource inclusion**: Images as `<Resource>`, SVG files as `<Content>` with `PreserveNewest`

### Provider Philosophy
- **No Essential Providers**: There are no hardcoded "essential" providers that the application depends on.
- **Key-Driven Activation**: A provider is considered active and essential only if the user has provided a valid API key (either via configuration or environment variables).
- **Listing**: The UI pre-populates a list of supported providers to allow users to easily add keys, but their underlying presence is merely structural until configured.
- **Equality**: All supported providers are treated equally in terms of system integration and display logic.

## Business Logic Rules

### Core Principles
These rules define the fundamental business logic that all AI agents MUST adhere to when working on this application.

#### 1. Provider Neutrality & Equality
- **RULE**: No provider is ever treated as "essential" or "required"
- **RULE**: All providers must be handled identically in business logic
- **RULE**: Never hardcode special treatment for specific providers
- **RULE**: The application must function correctly with zero configured providers
- **VIOLATION**: Any code that assumes certain providers exist or treats them differently

#### 2. Key-Driven Provider Activation
- **RULE**: A provider is only "active" when it has a valid API key in configuration or environment
- **RULE**: Never attempt API calls to providers without valid keys
- **RULE**: Missing keys should result in `IsAvailable = false` with descriptive message
- **RULE**: Providers listed in UI without keys are purely structural placeholders
- **IMPLEMENTATION**: Check `config.ApiKey` first in all `IProviderService.GetUsageAsync()` methods

#### 3. Graceful Degradation (Never Crash)
- **RULE**: Provider failures must NEVER crash the application
- **RULE**: Always return a valid `ProviderUsage` object, even on error
- **RULE**: Use `IsAvailable = false` for unavailable providers
- **RULE**: Continue processing other providers if one fails
- **RULE**: Distinguish between "missing config" (hide from UI) vs "errors" (show in UI)
- **IMPLEMENTATION**: Use try-catch in `ProviderManager.FetchAllUsageInternal()` (lines 102-137)

#### 4. Privacy & Security
- **RULE**: NEVER log API keys or any sensitive configuration data
- **RULE**: NEVER expose API keys in error messages shown to users
- **RULE**: All sensitive data must be encrypted before storage
- **RULE**: Use `System.Security.Cryptography.ProtectedData` for encryption
- **RULE**: Never include sensitive data in logs or telemetry
- **IMPLEMENTATION**: Use `ArgumentException` for validation, not exposing secrets

#### 5. Configuration Hierarchy & Discovery
- **RULE**: Support multiple configuration sources with defined precedence
- **RULE**: Environment variables take precedence over file-based config
- **RULE**: Check paths in order: `~/.ai-consumption-tracker/auth.json`, `~/.local/share/opencode/auth.json`, etc.
- **RULE**: Provider ID aliases must be resolved (e.g., "kimi-for-coding" → "kimi")
- **RULE**: `app_settings` key is reserved and must be excluded from provider list
- **IMPLEMENTATION**: `JsonConfigLoader.LoadConfigAsync()` (lines 12-110)

#### 6. Error Handling & Visibility
- **RULE**: `ArgumentException` → Hide provider from default UI (missing config)
- **RULE**: Other exceptions → Show provider with error state (network/API failure)
- **RULE**: Provide user-friendly descriptions, not technical stack traces
- **RULE**: Log all errors with context using `ILogger.LogError(ex, "message")`
- **IMPLEMENTATION**: `ProviderManager.FetchAllUsageInternal()` (lines 111-137)

#### 7. Validation & Pre-checks
- **RULE**: Validate all parameters before making API calls
- **RULE**: Throw `ArgumentException` for missing/invalid configuration
- **RULE**: Check for special key types (e.g., `sk-proj-` for OpenAI project keys)
- **RULE**: Validate required fields early in execution flow
- **IMPLEMENTATION**: Check `config.ApiKey` at start of `GetUsageAsync()` methods

#### 8. Cross-Platform Compatibility
- **RULE**: Core and Infrastructure projects MUST work on all platforms (Windows, Linux, macOS)
- **RULE**: Only UI project may be Windows-specific (WPF)
- **RULE**: CLI project MUST be cross-platform
- **RULE**: Never use Windows-specific APIs in Core/Infrastructure
- **VIOLATION**: Platform-specific code outside UI/CLI projects

#### 9. Data Integrity & Persistence
- **RULE**: Never lose user configuration on updates or errors
- **RULE**: Preserve `app_settings` when saving provider configurations
- **RULE**: Create backups before modifying critical configuration files
- **RULE**: Only save providers that have actual keys or base URLs
- **RULE**: Handle file I/O errors gracefully without corrupting data
- **IMPLEMENTATION**: `JsonConfigLoader.SaveConfigAsync()` (lines 112-156)

#### 10. Provider Manager Orchestration
- **RULE**: `ProviderManager` must coordinate all provider fetching
- **RULE**: Use `SemaphoreSlim` to prevent concurrent refreshes
- **RULE**: Support task deduplication (join existing refresh if in progress)
- **RULE**: Cache last results and support `forceRefresh` parameter
- **RULE**: Auto-add system providers that don't require auth (antigravity, gemini-cli, opencode-zen)
- **IMPLEMENTATION**: `ProviderManager.GetAllUsageAsync()` (lines 26-62)

#### 11. Generic Provider Fallback
- **RULE**: Unknown providers with `type: "pay-as-you-go"` or `type: "api"` must use `GenericPayAsYouGoProvider`
- **RULE**: Provider ID matching must be case-insensitive
- **RULE**: Support provider aliases (e.g., "claude" → "anthropic")
- **RULE**: Fallback to generic "Connected" status if no specific provider found
- **IMPLEMENTATION**: `ProviderManager.FetchAllUsageInternal()` (lines 86-150)

#### 12. Payment Type Handling
- **RULE**: Support all payment types: `UsageBased`, `Credits`, `Quota`
- **RULE**: Display usage based on payment type:
  - `UsageBased`: Show "Spent X / Limit Y"
  - `Credits`: Show "Remaining X"
  - `Quota`: Show "Used X / Limit Y"
- **RULE**: Set `IsQuotaBased = true` only for quota-based providers
- **IMPLEMENTATION**: Models defined in `ProviderConfig.cs` and `ProviderUsage.cs`

#### 13. User Preferences
- **RULE**: All user preferences must have sensible defaults
- **RULE**: Preferences stored in `auth.json` under `app_settings` key
- **RULE**: Support legacy `preferences.json` as fallback
- **RULE**: Never lose preferences on migration to new storage format
- **RULE**: Privacy mode must hide sensitive data from screenshots
- **IMPLEMENTATION**: `AppPreferences.cs` and `JsonConfigLoader.LoadPreferencesAsync()` (lines 161-191)

#### 14. Token Discovery
- **RULE**: Automatically discover tokens from common locations
- **RULE**: Discovered tokens supplement, don't replace, file-based config
- **RULE**: Don't overwrite existing config with discovered tokens unless key is empty
- **RULE**: Support environment variables for sensitive configuration
- **IMPLEMENTATION**: `TokenDiscoveryService` in `JsonConfigLoader.LoadConfigAsync()` (lines 91-107)

#### 15. Auto-Refresh Behavior
- **RULE**: Auto-refresh must be configurable (0 = disabled)
- **RULE**: Interval specified in seconds (`AutoRefreshInterval`)
- **RULE**: Only refresh when application is visible/active
- **RULE**: Respect privacy mode during refresh operations
- **IMPLEMENTATION**: UI timer logic using `AppPreferences.AutoRefreshInterval`

#### 16. Sub-Trays & Detailed Information
- **RULE**: Support provider-specific sub-trays for detailed breakdown
- **RULE**: Sub-trays controlled by `enabled_sub_trays` array in config
- **RULE**: Use `ProviderUsageDetail` for multi-level usage information
- **RULE**: Only show enabled sub-trays in UI
- **IMPLEMENTATION**: `ProviderUsageDetail` class and `ProviderConfig.EnabledSubTrays`

### Rule Enforcement Checklist
Before committing any changes to business logic, verify:

- [ ] Does this treat all providers equally?
- [ ] Does this crash if a provider fails?
- [ ] Does this expose sensitive data in logs or UI?
- [ ] Does this work on all supported platforms?
- [ ] Does this preserve user configuration?
- [ ] Does this handle missing/invalid keys gracefully?
- [ ] Does this follow the payment type display rules?
- [ ] Does this maintain the configuration hierarchy?

### Common Anti-Patterns to Avoid

❌ **Hardcoding provider lists**
```csharp
// BAD - Hardcoded provider list
var providers = new[] { "openai", "anthropic", "gemini" };
```

✅ **Dynamic provider discovery**
```csharp
// GOOD - Load from config
var configs = await _configLoader.LoadConfigAsync();
```

❌ **Throwing exceptions for provider failures**
```csharp
// BAD - Crashes on provider error
if (!response.IsSuccessStatusCode) 
    throw new HttpRequestException("Provider failed");
```

✅ **Returning error state**
```csharp
// GOOD - Graceful degradation
if (!response.IsSuccessStatusCode) 
    return new[] { new ProviderUsage { IsAvailable = false, Description = "Failed" } };
```

❌ **Logging sensitive data**
```csharp
// BAD - Exposes API key
_logger.LogInformation($"Checking provider with key: {config.ApiKey}");
```

✅ **Logging without secrets**
```csharp
// GOOD - Safe logging
_logger.LogDebug($"Checking provider: {config.ProviderId}");
```

### Provider Implementation Pattern
```csharp
public class ExampleProvider : IProviderService
{
    public string ProviderId => "example";
    private readonly HttpClient _httpClient;
    private readonly ILogger<ExampleProvider> _logger;

    public ExampleProvider(HttpClient httpClient, ILogger<ExampleProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            return new[] { new ProviderUsage
            {
                ProviderId = ProviderId,
                ProviderName = "Example",
                IsAvailable = false,
                Description = "API Key missing"
            }};
        }

        try
        {
            // Fetch usage...
            return new[] { /* usage data */ };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Provider check failed");
            return new[] { new ProviderUsage
            {
                ProviderId = ProviderId,
                ProviderName = "Example",
                IsAvailable = false,
                Description = "Connection Failed"
            }};
        }
    }
}
```

### JSON Handling
- Use `System.Text.Json` (not Newtonsoft)
- Configure with `JsonSerializerOptions` if needed
- Prefer `await HttpClient.GetFromJsonAsync<T>()` for GET requests
- Use `JsonSerializer.Serialize()` and `JsonContent.Create()` for POST

### Database & Storage
- **SQLite** via `Microsoft.Data.Sqlite`
- Encrypted storage using `System.Security.Cryptography.ProtectedData`
- Configuration stored in `auth.json` in app data directory
- Automatic backup created on updates

### Logging
- Use `Microsoft.Extensions.Logging` with structured logging
- Log levels: `LogDebug`, `LogInformation`, `LogWarning`, `LogError`
- Include context in messages: `LogDebug($"Fetching usage for provider: {config.ProviderId}")`

## Versioning
- Version numbers in `.csproj` files: `<Version>X.Y.Z</Version>`
- Update UI version for releases, other projects follow semantic versioning
- CI/CD triggered on tag push: `v*`
- **Release Notes**: Keep them concise. Do not repeat information already present in the change log. Focus on the high-level summary of changes.
- **Changelog**: Maintain a `CHANGELOG.md` file with concise documentation of changes for each version. Include the date of the release. Also keep an `## Unreleased` section at the top for tracking upcoming changes.

## Release Process

When preparing a new release (e.g., v1.5.0), ensure the following files are updated with the new version number:

### 1. Project Files (.csproj)
Update the `<Version>` tag in all project files:
- `AIConsumptionTracker.Core/AIConsumptionTracker.Core.csproj`
- `AIConsumptionTracker.Infrastructure/AIConsumptionTracker.Infrastructure.csproj`
- `AIConsumptionTracker.UI/AIConsumptionTracker.UI.csproj`
- `AIConsumptionTracker.CLI/AIConsumptionTracker.CLI.csproj`

### 2. Changelog
- Update `CHANGELOG.md`: Move the `## Unreleased` section to a new version header with the current date (e.g., `## [1.5.0] - 2026-02-06`).
- Ensure a new empty `## Unreleased` section is created at the top if needed for future tracking.

### 3. Documentation
- `README.md`: Update the version badge at the top: `![Version](https://img.shields.io/badge/version-1.5.0-blue)`
- `scripts/publish-app.ps1`: Update the example usage comment: `# Usage: .\scripts\publish-app.ps1 -Runtime win-x64 -Version 1.5.0`

### 4. Installer Setup
- `scripts/setup.iss`: Update the `MyAppVersion` definition: `#define MyAppVersion "1.5.0"`

### 5. Git Tagging
Once all files are committed and pushed to `main`, create a git tag to trigger the CI/CD release workflow:
```bash
git tag v1.5.0
git push origin v1.5.0
```

## CI/CD
- GitHub Actions for testing on push/PR to main.
- Release workflow creates installers for multiple platforms.
- Winget submission for Windows packages.
