# AI Consumption Tracker - Architecture Design Document

## Overview

AI Consumption Tracker is a .NET 8.0 WPF desktop application designed to monitor AI API usage, costs, and quotas across multiple providers. The architecture follows **Clean Architecture** principles with clear separation of concerns, dependency inversion, and extensibility through the provider pattern.

## Project Structure

```
AIConsumptionTracker/
├── AIConsumptionTracker.Core/          # Domain layer
├── AIConsumptionTracker.Infrastructure/ # Infrastructure layer
├── AIConsumptionTracker.UI.Slim/       # Presentation layer (WPF)
├── AIConsumptionTracker.CLI/           # Console interface
├── AIConsumptionTracker.Tests/         # Unit tests
└── AIConsumptionTracker.Web.Tests/     # Web tests
```

## Architectural Patterns

### 1. Clean Architecture / Layered Architecture

The solution is organized into distinct layers with clear dependencies:

- **Core Layer**: Contains domain models, interfaces, and business logic. Has no external dependencies.
- **Infrastructure Layer**: Implements external concerns (HTTP clients, file system, providers). Depends on Core.
- **UI Layer**: WPF application with views and view logic. Depends on Core and Infrastructure.
- **CLI Layer**: Console application sharing business logic. Depends on Core and Infrastructure.

### 2. Dependency Injection (DI)

Uses Microsoft's built-in DI container (`Microsoft.Extensions.DependencyInjection`):

```csharp
// Registration in App.xaml.cs
services.AddHttpClient();
services.AddSingleton<IConfigLoader, JsonConfigLoader>();
services.AddTransient<IProviderService, OpenAIProvider>();
services.AddSingleton<ProviderManager>();
```

**Benefits:**
- Loose coupling between components
- Easy testing with mock implementations
- Centralized configuration
- Lifecycle management (singleton, transient, scoped)

### 3. Provider Pattern

The core abstraction for integrating AI service providers:

```csharp
public interface IProviderService
{
    string ProviderId { get; }
    Task<IEnumerable<ProviderUsage>> GetUsageAsync(
        ProviderConfig config, 
        Action<ProviderUsage>? progressCallback = null);
}
```

**Key Characteristics:**
- Each provider implements the same interface
- Providers are discovered via DI container
- No hardcoded "essential" providers - all are treated equally
- Key-driven activation (provider is active only if API key is configured)

### 4. Repository Pattern

Configuration persistence via `IConfigLoader`:

```csharp
public interface IConfigLoader
{
    Task<List<ProviderConfig>> LoadConfigAsync();
    Task SaveConfigAsync(List<ProviderConfig> configs);
    Task<AppPreferences> LoadPreferencesAsync();
    Task SavePreferencesAsync(AppPreferences preferences);
}
```

## Core Components

### ProviderManager (Orchestrator)

Central service that coordinates all providers:

- **Responsibilities:**
  - Loads configurations
  - Discovers API keys from multiple sources
  - Executes provider calls in parallel
  - Caches results
  - Handles errors gracefully

- **Concurrency Control:**
  ```csharp
  private readonly SemaphoreSlim _httpSemaphore = new(6);
  ```

### Provider Implementations

15+ providers for various AI services:

| Provider | Type | Discovery |
|----------|------|-----------|
| OpenAIProvider | API Key | Environment, Config |
| ClaudeCodeProvider | API + CLI | Credentials file |
| ZaiProvider | API Key | Config |
| AntigravityProvider | Local Process | Process scan |
| GitHubCopilotProvider | OAuth | GitHub CLI |
| GenericPayAsYouGoProvider | Generic | Config |

### Configuration System

**Hierarchy:**
1. Primary config: `~/.ai-consumption-tracker/auth.json`
2. OpenCode config: `~/.local/share/opencode/auth.json`
3. Environment variables: `OPENAI_API_KEY`, etc.
4. IDE integrations: Kilo Code, Roo Code, Claude Code
5. CLI tools: GitHub CLI

**TokenDiscoveryService:**
- Scans environment variables
- Reads IDE configuration files
- Checks CLI tool storage
- Auto-discovers providers without manual configuration

## Data Models

### ProviderUsage

```csharp
public class ProviderUsage
{
    public string ProviderId { get; set; } = "";
    public string ProviderName { get; set; } = "";
    public double UsagePercentage { get; set; }
    public double CostUsed { get; set; }
    public double CostLimit { get; set; }
    public string UsageUnit { get; set; } = "credits";
    public bool IsQuotaBased { get; set; }
    public PaymentType PaymentType { get; set; }
    public bool IsAvailable { get; set; }
    public string Description { get; set; } = "";
    public List<ProviderUsageDetail> Details { get; set; } = new();
    public string? AccountName { get; set; }
}
```

### Payment Types

Three distinct payment models with different UI behaviors:

1. **UsageBased** (Postpaid)
   - Shows spending accumulation
   - High usage = red/warning
   - Examples: OpenAI, Claude

2. **Credits** (Prepaid)
   - Shows remaining balance
   - Low balance = red/warning
   - Examples: Synthetic, OpenCode

3. **Quota** (Recurring)
   - Shows remaining quota
   - Low quota = red/warning
   - Examples: Z.AI, some API limits

## UI Architecture

### MVVM-Style Pattern

While not strict MVVM, the architecture separates concerns:

- **Views**: XAML files define the UI structure
- **Code-Behind**: Handles UI logic and data binding
- **Services**: Business logic via DI

### Windows

| Window | Purpose | Key Features |
|--------|---------|--------------|
| MainWindow | Dashboard | Provider bars, progress indicators, privacy mode |
| SettingsWindow | Configuration | Tabbed interface, font selection, collapsible sections |
| InfoDialog | About | Version info, links |
| ProgressWindow | Updates | Download progress with NetSparkle |
| GitHubLoginDialog | Auth | Device flow authentication |

### Key UI Features

- **Privacy Mode**: Masks account names (`t**t@example.com`)
- **Collapsible Sections**: Plans & Quotas, Pay As You Go
- **Tray Icons**: Dynamic generation per provider
- **Auto-refresh**: Configurable interval (default 300s)
- **Compact vs Standard**: Toggle between views

## Data Flow

```
[User Action / Timer]
    ↓
[MainWindow.RefreshData()] / Auto-refresh
    ↓
[ProviderManager.GetAllUsageAsync()]
    ↓
[Load Configs] → JsonConfigLoader.LoadConfigAsync()
    ↓
[Discover Tokens] → TokenDiscoveryService.DiscoverTokens()
    ↓
[Parallel Provider Calls] → IProviderService.GetUsageAsync()
    ↓
[Progress Callbacks] → UpdateProviderBar()
    ↓
[Render UI] → MainWindow.RenderUsages()
```

## Extension Points

### Adding a New Provider

1. Create class implementing `IProviderService`:
   ```csharp
   public class NewProvider : IProviderService
   {
       public string ProviderId => "new-provider";
       
       public async Task<IEnumerable<ProviderUsage>> GetUsageAsync(
           ProviderConfig config, Action<ProviderUsage>? progressCallback)
       {
           // Implementation
       }
   }
   ```

2. Register in DI container (App.xaml.cs):
   ```csharp
   services.AddTransient<IProviderService, NewProvider>();
   ```

3. Add logo to `Assets/ProviderLogos/`

4. Add tests in `AIConsumptionTracker.Tests/`

### Adding a New Configuration Source

Extend `TokenDiscoveryService` to scan additional locations:
- New environment variables
- Additional config file formats
- Registry keys
- Cloud secret managers

## Testing Strategy

### Unit Tests

- **Framework**: xUnit with Moq
- **Pattern**: Arrange-Act-Assert
- **Coverage**: Business logic, providers, configuration

### UI Tests

- **Framework**: Xunit.StaFact for STA thread support
- **Approach**: Headless WPF testing
- **Coverage**: UI state, privacy mode, settings

### Mocking

```csharp
public class MockProviderService : IProviderService
{
    public string ProviderId { get; set; } = "mock";
    public Func<ProviderConfig, Task<IEnumerable<ProviderUsage>>>? UsageHandler { get; set; }
}
```

## Technology Stack

| Category | Technology |
|----------|------------|
| Framework | .NET 8.0 |
| UI | WPF (Windows-only) |
| DI | Microsoft.Extensions.DependencyInjection |
| HTTP | HttpClient |
| JSON | System.Text.Json |
| Testing | xUnit, Moq, Xunit.StaFact |
| Notifications | Windows Toast |
| Updates | NetSparkleUpdater |
| Database | SQLite (Microsoft.Data.Sqlite) |
| Encryption | ProtectedData |

## Important Design Decisions

### 1. No Essential Providers
All providers are treated equally. No hardcoded dependencies on specific providers.

### 2. Key-Driven Activation
A provider is considered active only if the user has configured a valid API key.

### 3. Graceful Degradation
Providers that fail to respond don't crash the application. They simply return `IsAvailable = false`.

### 4. Concurrent Provider Execution
All providers are queried in parallel for performance, with semaphore limiting to prevent overwhelming the system.

### 5. Caching Strategy
- Config cache: 5-second validity
- Provider results: Cached in ProviderManager
- Local process checks: Cached for Antigravity

### 6. Privacy by Design
- Privacy mode masks sensitive data
- API keys never displayed in UI
- Secure storage using ProtectedData

## Security Considerations

- API keys stored encrypted (ProtectedData)
- Privacy mode for screen sharing
- No logging of sensitive data
- Secure HTTPS only for API calls
- Device flow for OAuth (no password storage)

## Performance Considerations

- Parallel provider calls
- HTTP concurrency limiting (6 concurrent)
- Config caching
- Lazy loading of provider data
- Minimal UI updates (progress callbacks)

## Future Extensibility

The architecture supports:
- New AI providers via provider pattern
- Additional UI platforms (MAUI, Avalonia)
- Cloud sync of preferences
- Web dashboard
- Mobile companion app
- Additional notification channels
