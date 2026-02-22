# Important Rules and Guidelines

## Code Organization Rules

### 1. File-Scoped Namespaces
**Rule**: Always use file-scoped namespace declarations.

**Correct:**
```csharp
namespace AIUsageTracker.Core.Models;

public class ProviderUsage { }
```

**Incorrect:**
```csharp
namespace AIUsageTracker.Core.Models
{
    public class ProviderUsage { }
}
```

### 2. Using Statement Ordering
**Rule**: Group using statements in this order, separated by blank lines:
1. System
2. Third-party libraries
3. Project references

**Example:**
```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
```

### 3. Nullable Reference Types
**Rule**: Always handle potential nulls explicitly.

**Correct:**
```csharp
public string? ApiKey { get; set; }
public bool ShowAll { get; set; } = false;

if (config.ApiKey != null)
{
    // Use config.ApiKey
}
```

**Incorrect:**
```csharp
public string ApiKey { get; set; }  // Warning: non-nullable
```

## Naming Conventions

### Classes and Interfaces
- **Classes**: PascalCase (e.g., `ProviderManager`, `AppPreferences`)
- **Interfaces**: I-prefix + PascalCase (e.g., `IProviderService`, `IConfigLoader`)
- **Methods**: PascalCase (e.g., `GetUsageAsync`, `LoadConfigAsync`)
- **Properties**: PascalCase (e.g., `ProviderId`, `IsAvailable`)
- **Private Fields**: _camelCase with underscore prefix (e.g., `_httpClient`, `_logger`)

### Async Methods
**Rule**: Async methods must end with `Async` suffix.

**Correct:**
```csharp
public async Task<IEnumerable<ProviderUsage>> GetUsageAsync() { }
public async Task LoadConfigAsync() { }
```

**Incorrect:**
```csharp
public async Task<IEnumerable<ProviderUsage>> GetUsage() { }
```

### Boolean Properties
**Rule**: Use affirmative phrasing.

**Correct:**
```csharp
public bool IsAvailable { get; set; }
public bool StayOpen { get; set; }
```

**Incorrect:**
```csharp
public bool IsNotAvailable { get; set; }  // Double negative
public bool DoNotClose { get; set; }      // Confusing
```

## Formatting Rules

### Indentation and Braces
- **Indentation**: 4 spaces (no tabs)
- **Braces**: Allman style (opening brace on new line)
- **Line Length**: Aim for ~120 characters, max 150

**Example:**
```csharp
public class ExampleProvider : IProviderService
{
    public string ProviderId => "example";

    public async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config)
    {
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            return new[] { new ProviderUsage { IsAvailable = false } };
        }

        // Implementation
    }
}
```

### Blank Lines
- One blank line between methods
- Two blank lines between logical sections
- No blank lines after opening brace or before closing brace

### Object Initializers
**Rule**: Use object initializers for new objects.

**Correct:**
```csharp
return new ProviderUsage
{
    ProviderId = ProviderId,
    ProviderName = "Example",
    IsAvailable = true
};
```

**Incorrect:**
```csharp
var usage = new ProviderUsage();
usage.ProviderId = ProviderId;
usage.ProviderName = "Example";
usage.IsAvailable = true;
return usage;
```

## Error Handling Rules

### Exception Types
**Rule**: Use specific exception types with descriptive messages.

**Correct:**
```csharp
throw new ArgumentException("API key cannot be null or empty", nameof(apiKey));
```

**Incorrect:**
```csharp
throw new Exception("Invalid input");  // Too generic
```

### Provider Error Handling
**Rule**: Providers must return error states in objects, not throw exceptions.

**Correct:**
```csharp
try
{
    // Fetch usage...
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
```

**Incorrect:**
```csharp
try
{
    // Fetch usage...
}
catch (Exception ex)
{
    throw;  // Don't throw from providers
}
```

### Async Void
**Rule**: Never use `async void` except for event handlers.

**Correct:**
```csharp
public async Task DoWorkAsync() { }  // Return Task

// Event handler is OK
private async void Button_Click(object sender, EventArgs e) { }
```

**Incorrect:**
```csharp
public async void DoWorkAsync() { }  // Never do this
```

## Async/Await Patterns

### Always Await
**Rule**: Always await async calls. Never use `.Result` or `.Wait()`.

**Correct:**
```csharp
var result = await GetUsageAsync(config);
```

**Incorrect:**
```csharp
var result = GetUsageAsync(config).Result;  // Deadlock risk
```

### ConfigureAwait
**Rule**: Use `ConfigureAwait(false)` in library code (non-UI).

**Correct:**
```csharp
// In Infrastructure layer
var response = await _httpClient.GetAsync(url).ConfigureAwait(false);
```

**Correct:**
```csharp
// In UI layer - don't use ConfigureAwait
var result = await LoadDataAsync();
UpdateUI(result);
```

### Cancellation Tokens
**Rule**: Pass `CancellationToken` for long-running operations.

**Correct:**
```csharp
public async Task<IEnumerable<ProviderUsage>> GetUsageAsync(
    ProviderConfig config, 
    CancellationToken cancellationToken = default)
{
    await Task.Delay(1000, cancellationToken);
}
```

## Dependency Injection Rules

### Constructor Injection
**Rule**: Use constructor injection only. No property injection.

**Correct:**
```csharp
public class ProviderManager
{
    private readonly IEnumerable<IProviderService> _providers;
    private readonly ILogger<ProviderManager> _logger;

    public ProviderManager(
        IEnumerable<IProviderService> providers,
        ILogger<ProviderManager> logger)
    {
        _providers = providers;
        _logger = logger;
    }
}
```

**Incorrect:**
```csharp
public class ProviderManager
{
    [Inject]  // Don't use property injection
    public IConfigLoader ConfigLoader { get; set; } = null!;
}
```

### Readonly Fields
**Rule**: All injected dependencies should be readonly.

**Correct:**
```csharp
private readonly HttpClient _httpClient;
private readonly ILogger<OpenAIProvider> _logger;
```

**Incorrect:**
```csharp
private HttpClient _httpClient;  // Should be readonly
```

## Logging Rules

### Use Structured Logging
**Rule**: Use `ILogger<T>` with structured messages.

**Correct:**
```csharp
_logger.LogInformation("Fetching usage for provider: {ProviderId}", config.ProviderId);
_logger.LogError(ex, "Failed to fetch usage for {ProviderId}", config.ProviderId);
```

**Incorrect:**
```csharp
Console.WriteLine($"Fetching usage for {config.ProviderId}");  // Never use Console
_logger.LogInformation($"Fetching usage for {config.ProviderId}");  // Don't interpolate
```

### No Secrets in Logs
**Rule**: Never log API keys, tokens, or passwords.

**Correct:**
```csharp
_logger.LogInformation("API key configured: {IsConfigured}", !string.IsNullOrEmpty(apiKey));
```

**Incorrect:**
```csharp
_logger.LogInformation("API key: {ApiKey}", apiKey);  // NEVER DO THIS
```

## Provider Implementation Rules

### Provider Interface Contract
**Rule**: All providers must implement `IProviderService` exactly.

**Required:**
```csharp
public class MyProvider : IProviderService
{
    public string ProviderId => "my-provider";
    
    public async Task<IEnumerable<ProviderUsage>> GetUsageAsync(
        ProviderConfig config, 
        Action<ProviderUsage>? progressCallback = null)
    {
        // Implementation
    }
}
```

### Graceful Degradation
**Rule**: Providers must handle missing API keys gracefully.

**Correct:**
```csharp
if (string.IsNullOrEmpty(config.ApiKey))
{
    return new[] { new ProviderUsage
    {
        ProviderId = ProviderId,
        ProviderName = "My Provider",
        IsAvailable = false,
        Description = "API key not configured"
    }};
}
```

### Progress Callbacks
**Rule**: Call progress callback when data is available.

**Correct:**
```csharp
var usage = await FetchUsageAsync();
progressCallback?.Invoke(usage);
return new[] { usage };
```

## Testing Rules

### Test Naming
**Rule**: Use descriptive test names following `[Method]_[Scenario]_[ExpectedResult]` pattern.

**Correct:**
```csharp
[Fact]
public async Task GetAllUsageAsync_LoadsConfigAndFetchesUsageFromMocks()
[Fact]
public void ParseOutput_WithValidInput_ReturnsCorrectUsage()
```

**Incorrect:**
```csharp
[Fact]
public void Test1()  // Not descriptive
```

### Arrange-Act-Assert
**Rule**: Structure tests with clear AAA sections.

**Correct:**
```csharp
[Fact]
public async Task GetUsageAsync_WithValidKey_ReturnsUsage()
{
    // Arrange
    var config = new ProviderConfig { ApiKey = "test-key" };
    var provider = new MockProviderService();

    // Act
    var result = await provider.GetUsageAsync(config);

    // Assert
    Assert.True(result.First().IsAvailable);
}
```

### Mock External Dependencies
**Rule**: Always mock external services (HTTP, file system, etc.).

**Correct:**
```csharp
var mockHttp = new Mock<HttpMessageHandler>();
mockHttp.Protected()
    .Setup<Task<HttpResponseMessage>>("SendAsync", ...)
    .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.OK });
```

## WPF/UI Rules

### Code-Behind Guidelines
**Rule**: Keep code-behind minimal. Use services for business logic.

**Correct:**
```csharp
// In MainWindow.xaml.cs
private async void RefreshButton_Click(object sender, RoutedEventArgs e)
{
    await _providerManager.RefreshAsync();
}
```

**Incorrect:**
```csharp
// Don't put business logic in code-behind
private void RefreshButton_Click(object sender, RoutedEventArgs e)
{
    // Complex logic here
}
```

### STA Thread for UI Tests
**Rule**: Use `WpfFact` or `StaFact` for WPF tests.

**Correct:**
```csharp
[WpfFact]
public void SavingSettings_ShouldSetSettingsChanged()
{
    // Test code
}
```

**Incorrect:**
```csharp
[Fact]  // Will fail on UI thread
public void SavingSettings_ShouldSetSettingsChanged()
```

## Version Control Rules

### Git Workflow
**Rule**: Never push directly to `main`. Use feature branches and PRs.

**Process:**
1. Create feature branch: `git checkout -b feature/my-feature`
2. Make changes and commit
3. Push branch: `git push -u origin feature/my-feature`
4. Create Pull Request
5. After review, merge to main

### Commit Messages
**Rule**: Use conventional commit format.

**Format:**
```
<type>(<scope>): <description>

[optional body]

[optional footer]
```

**Examples:**
```
feat(provider): add support for Mistral AI API

fix(ui): resolve progress bar color calculation for quota-based providers

chore(release): bump version to 1.8.4
```

### Types:
- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation changes
- `style`: Code style changes (formatting, semicolons)
- `refactor`: Code refactoring
- `test`: Adding or updating tests
- `chore`: Build process, dependencies, etc.

## Security Rules

### API Key Storage
**Rule**: API keys must be stored encrypted.

**Correct:**
```csharp
var encrypted = ProtectedData.Protect(
    Encoding.UTF8.GetBytes(apiKey), 
    entropy, 
    DataProtectionScope.CurrentUser);
```

### Privacy Mode
**Rule**: Implement privacy mode for all sensitive data display.

**Correct:**
```csharp
public static string MaskEmail(string email)
{
    if (string.IsNullOrEmpty(email) || !email.Contains('@'))
        return email;

    var parts = email.Split('@');
    var local = parts[0];
    var domain = parts[1];

    if (local.Length <= 2)
        return $"{local[0]}***@{domain}";

    return $"{local[0]}***{local[^1]}@{domain}";
}
```

## Performance Rules

### HttpClient Usage
**Rule**: Use singleton HttpClient or IHttpClientFactory. Never create new instances per request.

**Correct:**
```csharp
// In DI registration
services.AddHttpClient();

// In constructor
public MyService(HttpClient httpClient) { }
```

**Incorrect:**
```csharp
// Don't do this
using var client = new HttpClient();
```

### Async All the Way
**Rule**: Don't block on async code.

**Correct:**
```csharp
public async Task LoadDataAsync()
{
    var data = await FetchDataAsync();
}
```

**Incorrect:**
```csharp
public void LoadData()
{
    var data = FetchDataAsync().Result;  // Deadlock!
}
```

## CI/CD Rules

### Workflow Files
**Rule**: Always test workflow changes locally when possible.

### Version Updates
**Rule**: Update all version files simultaneously:
- All `.csproj` files
- `README.md` badge
- `CHANGELOG.md`
- `scripts/setup.iss`
- `scripts/publish-app.ps1`

### Release Process
**Rule**: Always use PRs for version bumps, never direct commits to main.

1. Create feature branch
2. Update version files
3. Create PR
4. Merge PR
5. Tag the release

