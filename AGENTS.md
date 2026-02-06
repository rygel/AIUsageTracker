# AI Consumption Tracker - Agent Guidelines

This document provides essential information for agentic coding assistants working on this .NET 8.0 WPF application.

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
