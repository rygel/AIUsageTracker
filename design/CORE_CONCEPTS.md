# Core Concepts

## 1. Provider-Centric Architecture

The entire application revolves around the concept of **Providers** - AI services that expose usage and quota information.

### Provider Identity
Each provider has a unique `ProviderId` that:
- Identifies the provider throughout the system
- Links configuration to implementation
- Enables provider-specific UI rendering

### Provider States
- **Available**: Provider is responding with valid data
- **Unavailable**: No API key configured or provider error
- **Error**: Provider returned an error state

## 2. Key-Driven Activation

**Philosophy**: A provider is considered "active" only if the user has provided a valid API key.

### Key Sources (Priority Order)
1. **Explicit Configuration** (`auth.json`)
2. **Environment Variables** (e.g., `OPENAI_API_KEY`)
3. **IDE Integration** (Claude Code, Kilo Code credentials)
4. **CLI Tools** (GitHub CLI token)
5. **Auto-Discovery** (TokenDiscoveryService)

### Implications
- No hardcoded "essential" providers
- All providers are optional
- UI dynamically shows only configured providers
- "Show All" mode can display inactive providers

## 3. Payment Type Model

Three distinct payment models drive different UI behaviors:

### UsageBased (Postpaid)
- **Concept**: Pay for what you use after usage
- **UI Behavior**: Progress bar fills as spending increases
- **Warning**: High percentage = danger (overspending)
- **Examples**: OpenAI, Claude, most APIs
- **Calculation**: `(used / limit) * 100`

### Credits (Prepaid)
- **Concept**: Pre-purchased credits/balance
- **UI Behavior**: Progress bar shows remaining credits
- **Warning**: Low percentage = danger (running out)
- **Examples**: Synthetic, some OpenCode providers
- **Calculation**: `(remaining / total) * 100`

### Quota (Recurring)
- **Concept**: Periodic allowance (daily/monthly)
- **UI Behavior**: Progress bar shows remaining quota
- **Warning**: Low percentage = danger (quota exhausted)
- **Examples**: Z.AI, rate limits
- **Calculation**: `(remaining / total) * 100`

## 4. Configuration Hierarchy

Configuration follows a layered approach with override capability:

```
System Defaults
    ↓
Application Config (auth.json)
    ↓
User Preferences (preferences.json)
    ↓
Environment Variables
    ↓
Auto-Discovered Keys
```

### Configuration Files

**auth.json**:
```json
{
  "providers": [
    {
      "providerId": "openai",
      "apiKey": "sk-...",
      "baseUrl": "https://api.openai.com/v1",
      "paymentType": "usageBased"
    }
  ]
}
```

**preferences.json**:
```json
{
  "windowWidth": 420,
  "windowHeight": 500,
  "privacyMode": false,
  "autoRefreshInterval": 300,
  "collapsedSections": ["pay-as-you-go"]
}
```

## 5. Progressive Disclosure

### Compact vs Standard Mode
- **Compact**: Minimal information, single progress bar
- **Standard**: Full details, multiple metrics, account info

### Collapsible Sections
- Plans & Quotas (quota-based providers)
- Pay As You Go (usage-based providers)
- State persisted in preferences

### Privacy Mode
- Masks account names: `test@example.com` → `t**t@example.com`
- Hides detailed descriptions
- Useful for screen sharing/streaming

## 6. Concurrent Data Fetching

### Parallel Execution
All providers are queried simultaneously:
```csharp
var tasks = configs.Select(async config => {
    var provider = GetProvider(config.ProviderId);
    return await provider.GetUsageAsync(config);
});
var results = await Task.WhenAll(tasks);
```

### Concurrency Control
Semaphore limits HTTP requests to prevent overwhelming:
```csharp
private readonly SemaphoreSlim _httpSemaphore = new(6);
```

### Progress Callbacks
Providers report progress for UI updates:
```csharp
Action<ProviderUsage>? progressCallback = (usage) => {
    UpdateProviderBar(usage);
};
```

## 7. Resilience Patterns

### Graceful Degradation
- Individual provider failures don't crash the app
- Failed providers return `IsAvailable = false`
- Error information preserved in `Description`

### Retry Logic
- HTTP calls use Polly for automatic retries
- Exponential backoff for transient failures
- Circuit breaker pattern for persistent failures

### Caching
- Configuration cached for 5 seconds
- Provider results cached to avoid redundant calls
- Local process status cached (Antigravity)

## 8. Extensibility Model

### Plugin Architecture
New providers can be added without modifying existing code:

1. Implement `IProviderService`
2. Register in DI container
3. Add logo asset
4. Optionally add to TokenDiscoveryService

### Provider Fallback
Generic providers handle unknown configurations:
```csharp
if (provider == null && config.Type == "pay-as-you-go") {
    provider = _providers.FirstOrDefault(p => 
        p.ProviderId == "generic-pay-as-you-go");
}
```

## 9. Security Model

### Data Protection
- API keys encrypted using Windows DPAPI (ProtectedData)
- Keys never displayed in UI
- Secure storage in `%APPDATA%`

### Privacy Controls
- Privacy mode masks sensitive data
- No telemetry or analytics
- Local-only processing (no cloud dependencies)

### OAuth Device Flow
- GitHub authentication uses device flow
- No password storage
- Token refresh handled automatically

## 10. Update Model

### NetSparkle Integration
- Automatic update checks on startup
- Architecture-specific installers (x64, x86, arm64)
- Changelog displayed before update
- One-click download and install

### Appcast Files
```xml
<item>
    <title>Version 1.8.4</title>
    <sparkle:releaseNotesLink>...</sparkle:releaseNotesLink>
    <pubDate>...</pubDate>
    <enclosure url="..." sparkle:version="1.8.4" />
</item>
```

## 11. Testing Philosophy

### Test Pyramid
- **Unit Tests**: Business logic, providers (fast, isolated)
- **Integration Tests**: Provider APIs (slower, real calls)
- **UI Tests**: WPF components (headless, fast)

### Mock Strategy
- All external dependencies mocked
- HttpClient mocked with MockHttpMessageHandler
- File system mocked with temporary files

### Privacy Testing
- Verify masking in privacy mode
- Ensure sensitive data never logged
- Test data redaction

## 12. Performance Principles

### Lazy Loading
- Provider data fetched on-demand
- UI updates batched
- Images/assets loaded asynchronously

### Resource Management
- HttpClient reused (singleton)
- Cancellation tokens for timeouts
- Dispose patterns for unmanaged resources

### Memory Efficiency
- JSON streaming for large responses
- Image caching with size limits
- Weak references for event handlers
