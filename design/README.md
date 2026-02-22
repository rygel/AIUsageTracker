# AI Consumption Tracker - Design Documentation

This directory contains comprehensive design documentation for AI Consumption Tracker application.

## Documents

### [ARCHITECTURE.md](ARCHITECTURE.md)
Complete architectural overview including:
- Project structure and organization
- Architectural patterns (Clean Architecture, DI, Provider Pattern)
- Core components and responsibilities
- Data flow and extension points
- Technology stack

### [CORE_CONCEPTS.md](CORE_CONCEPTS.md)
Key conceptual models and design principles:
- Provider-centric architecture
- Key-driven activation
- Payment type model (UsageBased, Credits, Quota)
- Configuration hierarchy
- Progressive disclosure (Compact/Standard mode, Privacy)
- Concurrent data fetching
- Resilience patterns

### [RULES.md](RULES.md)
Coding standards and guidelines:
- Code organization (namespaces, usings)
- Naming conventions
- Formatting rules
- Error handling
- Async/await patterns
- Dependency injection
- Logging standards
- Testing guidelines
- WPF/UI rules
- Git workflow and commit conventions
- Security guidelines

### [FEATURES_KEYBOARD_SHORTCUTS.md](FEATURES_KEYBOARD_SHORTCUTS.md)
Feature documentation for keyboard shortcuts and theme system:
- Keyboard shortcut implementation (Ctrl+R, Ctrl+P, Ctrl+T, F2, Escape)
- Dark/Light theme system
- Theme persistence and toggle
- Color specifications for both themes

## Quick Reference

### Architecture Overview
```
┌─────────────────────────────────────────────┐
│                AIUsageTracker                │
│                                             │
│  ┌──────────────┐  ┌──────────────────┐    │
│  │      UI      │  │       CLI        │    │
│  │   (WPF)      │  │   (Console)      │    │
│  └──────┬───────┘  └────────┬─────────┘    │
│         │                   │              │
│         └─────────┬─────────┘              │
│                   │                        │
│         ┌─────────▼──────────┐             │
│         │   Infrastructure   │             │
│         │  (Providers, HTTP) │             │
│         └─────────┬──────────┘             │
│                   │                        │
│         ┌─────────▼──────────┐             │
│         │       Core         │             │
│         │ (Models, Interfaces)│            │
│         └────────────────────┘             │
└─────────────────────────────────────────────┘
```

### Key Patterns

1. **Provider Pattern**: All AI services implement `IProviderService`
2. **Dependency Injection**: Constructor injection with readonly fields
3. **Clean Architecture**: Layered with clear dependencies
4. **Repository Pattern**: Configuration via `IConfigLoader`

### Critical Rules

1. **Never push to main** - Always use feature branches and PRs
2. **No hardcoded essential providers** - All providers are optional
3. **Graceful degradation** - Providers never crash the app
4. **Always use ConfigureAwait(false)** in library code
5. **Never log API keys or secrets**
6. **Async methods must end with Async suffix**
7. **Use file-scoped namespaces**
8. **Handle nulls explicitly** (nullable reference types enabled)

### Adding a New Provider

1. Create class implementing `IProviderService`
2. Register in DI container (`App.xaml.cs`)
3. Add logo to `Assets/ProviderLogos/`
4. Add tests

Example:
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

## Contributing

When contributing to this project:
1. Read the [RULES.md](RULES.md) document
2. Follow the established patterns
3. Write tests for new features
4. Update documentation as needed
5. Use conventional commit messages

## Questions?

Refer to the specific documents for detailed information:
- Architecture questions → [ARCHITECTURE.md](ARCHITECTURE.md)
- Conceptual understanding → [CORE_CONCEPTS.md](CORE_CONCEPTS.md)
- Coding standards → [RULES.md](RULES.md)
