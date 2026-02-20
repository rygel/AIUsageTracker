# AI Consumption Tracker - Agent Guidelines

This document provides essential information for agentic coding assistants working on this .NET 8.0 WPF application.

## Development Workflow

- **Never push directly to `main`**: All changes, including release preparations, MUST be done on a feature branch (e.g., `feature/branch-name`) and integrated via a Pull Request.
- **Never force push to `main` without explicit user permission**: If you need to force push to main, ALWAYS ask for confirmation first.
- **Atomic Commits**: Keep commits focused and logically grouped.
- **CI/CD Compliance**: Ensure that any UI changes or tests are compatible with the headless CI environment.
- **No Icons in PRs**: When creating pull requests, do not use emojis or icons in the title or body.
- **PR Management**: ALWAYS modify existing PRs instead of closing and creating new ones. Keep work in the same PR to maintain conversation context and avoid PR number inflation.

## Project Structure

- **AIConsumptionTracker.Core**: Domain models, interfaces, and business logic (PCL)
- **AIConsumptionTracker.Infrastructure**: External providers, data access, configuration
- **AIConsumptionTracker.UI.Slim**: Lightweight WPF desktop application with compact UI
- **AIConsumptionTracker.Agent**: Background service that collects provider usage data via HTTP API
- **AIConsumptionTracker.CLI**: Console interface (cross-platform)
- **AIConsumptionTracker.Web**: ASP.NET Core Razor Pages web application for viewing data
- **AIConsumptionTracker.Tests**: xUnit unit tests with Moq mocking

## Build & Test Commands

### Building
```bash
# Build entire solution
dotnet build AIConsumptionTracker.slnx --configuration Debug

# Build specific project
dotnet build AIConsumptionTracker.UI.Slim/AIConsumptionTracker.UI.Slim.csproj

# Restore dependencies
dotnet restore
```

### Testing
```bash
# Run all unit tests
dotnet test AIConsumptionTracker.Tests/AIConsumptionTracker.Tests.csproj --configuration Debug

# Run all tests (no rebuild)
dotnet test --no-build --verbosity normal

# Run a single test
dotnet test --filter "FullyQualifiedName~GetAllUsageAsync_LoadsConfigAndFetchesUsageFromMocks"

# Run tests by class
dotnet test --filter "FullyQualifiedName~ProviderManagerTests"
```

### Running the Agent
```bash
# Run the Agent service
dotnet run --project AIConsumptionTracker.Agent

# Agent runs on port 5000 by default (auto-discovers available port 5000-5010)
# Port is saved to %LOCALAPPDATA%\AIConsumptionTracker\Agent\agent.port
```

### Running the Web UI
```bash
# Run the Web application (requires Agent to be running)
dotnet run --project AIConsumptionTracker.Web

# Web UI runs on port 5100
# Access at http://localhost:5100
```

### Running the Slim UI
```bash
# Run the Slim WPF application
dotnet run --project AIConsumptionTracker.UI.Slim

# Automatically discovers Agent port from agent.port file
# Falls back to ports 5000-5010 if discovery fails
```

### Automated Screenshots
To generate updated screenshots for documentation (headless and in Privacy Mode):
```bash
# Run from the UI bin directory or project root
AIConsumptionTracker.exe --test --screenshot
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

### Agent Architecture

The Agent is a background HTTP service that collects and stores provider usage data:

**Key Components:**
- **UsageDatabase.cs**: SQLite database with four tables (providers, provider_history, raw_snapshots, reset_events)
- **ProviderRefreshService.cs**: Scheduled refresh logic, filters providers without API keys
- **ConfigService.cs**: Configuration and preferences management

**Port Management:**
- Default port: 5000
- Auto-discovery: Tries ports 5000-5010, then random
- Port saved to: `%LOCALAPPDATA%\AIConsumptionTracker\Agent\agent.port`

**Database Schema:**
```
providers - Static provider configuration
provider_history - Time-series usage data (kept indefinitely)
raw_snapshots - Raw JSON data (14-day TTL, auto-cleanup)
reset_events - Quota/limit reset tracking (kept indefinitely)
```

### Web UI Architecture

The Web UI is an ASP.NET Core Razor Pages application that reads from the Agent's database:

**Features:**
- **Dashboard**: Stats cards, provider usage with progress bars, 60s auto-refresh
- **Providers**: Table view of all providers with status
- **Provider Details**: Individual history + reset events
- **History**: Complete usage history across all providers

**Technology Stack:**
- **Framework**: ASP.NET Core 8.0
- **Pattern**: Razor Pages (server-rendered)
- **Styling**: CSS variables for theming
- **Database**: Read-only access to Agent's SQLite database

**HTMX Integration:**
- Auto-refresh via `hx-trigger="every 60s"`
- Partial page updates without full reload
- CDN loaded from unpkg.com

**Theme System:**
Seven built-in themes using CSS variables:
- **Dark** (default): `#1e1e1e` background
- **Light**: Clean white background
- **High Contrast**: Pure black/white for accessibility
- **Solarized Dark**: Blue-green palette
- **Solarized Light**: Sepia-toned
- **Dracula**: Purple/pink highlights
- **Nord**: Frosty blue-gray tones

Theme toggle in navbar with localStorage persistence.

### Slim UI Port Discovery

The Slim UI automatically discovers the Agent port:

**Process:**
1. Read from `%LOCALAPPDATA%\AIConsumptionTracker\Agent\agent.port`
2. If not found, try port 5000
3. Try fallback ports 5001-5010
4. Use discovered port for all API calls

**Implementation:**
- `AgentService.RefreshPortAsync()` - Updates port before API calls
- `AgentLauncher.IsAgentRunningWithPortAsync()` - Returns (isRunning, port)
- Port is cached but refreshed on initialization

### Content Security Policy

CSP is configured in `Program.cs` with different policies for Development vs Production:

**Development:**
```csharp
script-src 'self' 'unsafe-inline' 'unsafe-eval' https://unpkg.com;
```
- Allows HTMX eval and Browser Link/Hot Reload

**Production:**
```csharp
script-src 'self' https://unpkg.com;
```
- Strict policy - requires self-hosted HTMX

### Provider Philosophy
- **No Essential Providers**: There are no hardcoded "essential" providers that the application depends on.
- **Key-Driven Activation**: A provider is considered active and essential only if the user has provided a valid API key (either via configuration or environment variables).
- **Listing**: The UI pre-populates a list of supported providers to allow users to easily add keys, but their underlying presence is merely structural until configured.
- **Equality**: All supported providers are treated equally in terms of system integration and display logic.

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

### Progress Bar Calculation for Payment Types

The application uses different progress bar calculations depending on the payment type to provide intuitive visual feedback:

#### Quota-Based Providers (e.g., Synthetic, Z.AI)

**Calculation:** Show **remaining percentage** (full bar = lots of credits remaining)
```csharp
var utilization = (total - used) / total * 100.0;
```

**Visual Behavior:**
- 0 used / 135 total = **100% full bar** (all credits available)
- 67 used / 135 total = **50% bar** (half credits remaining)
- 135 used / 135 total = **0% empty bar** (no credits remaining)

**Color Logic:**
- **Green**: UsagePercentage > ColorThresholdYellow (lots remaining)
- **Yellow**: ColorThresholdRed < UsagePercentage <= ColorThresholdYellow (moderate remaining)
- **Red**: UsagePercentage < ColorThresholdRed (dangerously low remaining)

**Rationale:** Users expect to see a full green bar when they have all their quota available. The bar depletes and turns red as they use credits, similar to a fuel gauge.

#### Credits-Based Providers (e.g., OpenCode)

**Calculation:** Show **used percentage** (full bar = high usage/spending)
```csharp
var utilization = used / total * 100.0;
```

**Visual Behavior:**
- 0 used / 100 total = **0% empty bar** (no spending yet)
- 50 used / 100 total = **50% bar** (moderate spending)
- 100 used / 100 total = **100% full bar** (budget exhausted)

**Color Logic:**
- **Green**: UsagePercentage < ColorThresholdYellow (low spending)
- **Yellow**: ColorThresholdYellow <= UsagePercentage < ColorThresholdRed (moderate spending)
- **Red**: UsagePercentage >= ColorThresholdRed (high spending/budget exhausted)

**Rationale:** For pay-as-you-go providers, users want to see spending accumulate. The bar fills up and turns red as they spend money, acting as a spending warning indicator.

#### Implementation

**Backend (Provider):**
```csharp
// For quota-based providers, show remaining percentage (full bar = lots remaining)
// For other providers, show used percentage (full bar = high usage)
var utilization = paymentType == PaymentType.Quota
    ? (total > 0 ? ((total - used) / total) * 100.0 : 100)  // Remaining % for quota
    : (total > 0 ? (used / total) * 100.0 : 0);              // Used % for others
```

**Frontend (UI Color Logic):**
```csharp
// For quota-based providers: high remaining % = green (good), low = red (bad)
// For usage-based: high used % = red (bad), low = green (good)
var isQuota = usage.IsQuotaBased || usage.PaymentType == PaymentType.Quota;
var brush = isQuota
    ? (usage.UsagePercentage < ColorThresholdRed ? Brushes.Crimson 
        : (usage.UsagePercentage < ColorThresholdYellow ? Brushes.Gold : Brushes.MediumSeaGreen))
    : (usage.UsagePercentage > ColorThresholdRed ? Brushes.Crimson 
        : (usage.UsagePercentage > ColorThresholdYellow ? Brushes.Gold : Brushes.MediumSeaGreen));
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

IMPORTANT: **All release-related changes MUST be made via pull request**. Never trigger the release workflow directly on main. Always:
1. Create a feature branch (e.g., `feature/v1.7.13-release`)
2. Update version files on that branch
3. Create a pull request to main
4. After PR is merged, trigger the release workflow with `skip_file_updates=true`

When preparing a new release (e.g., v1.5.0), ensure the following files are updated with the new version number:

### 1. Project Files (.csproj)
Update the `<Version>` tag in all project files:
- `AIConsumptionTracker.Core/AIConsumptionTracker.Core.csproj`
- `AIConsumptionTracker.Infrastructure/AIConsumptionTracker.Infrastructure.csproj`
- `AIConsumptionTracker.UI.Slim/AIConsumptionTracker.UI.Slim.csproj`
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

### 6. Appcast Files (Updater)
After the release workflow completes and assets are published, update the appcast files in the repository root to point to the new release assets:

**Appcast files to update:**
- `appcast.xml` - Default (x64)
- `appcast_x64.xml` - x64 architecture
- `appcast_arm64.xml` - ARM64 architecture
- `appcast_x86.xml` - x86 architecture

**Important: Match the exact asset filenames from the release:**
```bash
# Check release assets
gh release view v1.5.0 --json assets --jq '.assets[].name'
```

**URL pattern:** `https://github.com/rygel/AIConsumptionTracker/releases/download/v1.5.5/AIConsumptionTracker_Setup_v1.5.5_win-x64.exe`

Note: Release assets use `-win-x64`, `-win-arm64`, `-win-x86` suffixes (NOT `-x64`, `-arm64`, `-x86`).

**Update appcast entries:**
```xml
<item>
    <title>Version 1.5.0</title>
    <sparkle:releaseNotesLink>https://github.com/rygel/AIConsumptionTracker/releases/tag/v1.5.0</sparkle:releaseNotesLink>
    <pubDate>Wed, 11 Feb 2026 19:45:00 +0000</pubDate>
    <enclosure url="https://github.com/rygel/AIConsumptionTracker/releases/download/v1.5.0/AIConsumptionTracker_Setup_v1.5.0_win-x64.exe"
               sparkle:version="1.5.0"
               sparkle:os="windows"
               length="0"
               type="application/octet-stream" />
</item>
```

Commit and push the appcast updates after the release assets are available.

## CI/CD
- GitHub Actions for testing on push/PR to main.
- Release workflow creates installers for multiple platforms.
- Winget submission for Windows packages.
