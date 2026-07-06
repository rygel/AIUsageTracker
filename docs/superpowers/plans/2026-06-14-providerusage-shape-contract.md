# ProviderUsage Shape Contract Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the 36-property `ProviderUsage` superset class with a sealed subtype hierarchy (abstract base + 4 concrete subtypes).

**Architecture:** `ProviderUsage` becomes abstract with ~12 universal fields. `StatusProviderUsage` (sealed) has no usage data. `QuotaProviderUsage` (not sealed) adds quota fields. `WindowedProviderUsage : QuotaProviderUsage` (sealed) adds window fields. `ModelScopedProviderUsage : QuotaProviderUsage` (sealed) adds model fields. JSON polymorphic serialization via `[JsonDerivedType]`. Pattern matching replaces null-checking at consumer boundaries.

**Tech Stack:** .NET 10, System.Text.Json polymorphic serialization, SQLite, xUnit + Moq

---

## File Structure

### Create
- `AIUsageTracker.Core/Models/QuotaProviderUsage.cs` — sealed subclass inheriting from ProviderUsage
- `AIUsageTracker.Core/Models/WindowedProviderUsage.cs` — sealed subclass inheriting from QuotaProviderUsage
- `AIUsageTracker.Core/Models/ModelScopedProviderUsage.cs` — sealed subclass inheriting from QuotaProviderUsage
- `AIUsageTracker.Core/Models/StatusProviderUsage.cs` — sealed subclass inheriting from ProviderUsage

### Modify
- `AIUsageTracker.Core/Models/ProviderUsage.cs` — add `[JsonDerivedType]` attributes, make abstract, remove subtype-only properties
- `AIUsageTracker.Core/Providers/ProviderBase.cs` — add typed factory methods, refactor existing helpers
- 14 provider implementation files — switch from `new ProviderUsage { ... }` to typed factories
- Monitor persistence layer — handle `card_type` on read/write
- Slim UI consumer files — swap null-checks for pattern matches
- Web UI consumer files — same
- CLI consumer files — same
- Architecture guardrail tests — ensure removed base properties aren't referenced

---

### Task 1: Make ProviderUsage abstract and define sealed subtypes

**Files:**
- Modify: `AIUsageTracker.Core/Models/ProviderUsage.cs:1-225`
- Create: `AIUsageTracker.Core/Models/QuotaProviderUsage.cs`
- Create: `AIUsageTracker.Core/Models/WindowedProviderUsage.cs`
- Create: `AIUsageTracker.Core/Models/ModelScopedProviderUsage.cs`
- Create: `AIUsageTracker.Core/Models/StatusProviderUsage.cs`
- Test: Run `dotnet build` to verify compilation

- [ ] **Step 1: Add `[JsonDerivedType]` attributes and make ProviderUsage abstract**

```csharp
// Remove `public class ProviderUsage` — replace with:
[JsonDerivedType(typeof(QuotaProviderUsage), typeDiscriminator: "quota")]
[JsonDerivedType(typeof(WindowedProviderUsage), typeDiscriminator: "windowed")]
[JsonDerivedType(typeof(ModelScopedProviderUsage), typeDiscriminator: "model")]
[JsonDerivedType(typeof(StatusProviderUsage), typeDiscriminator: "status")]
public abstract class ProviderUsage
{
    // Keep only: ProviderId, ProviderName, AccountName, ConfigKey, AuthSource
    // Keep only: IsAvailable, State, Description, FetchedAt, IsStale
    // Keep only: HttpStatus, ResponseLatencyMs, RawJson, FailureContext,
    //            UpstreamResponseValidity, UpstreamResponseNote
    // Keep only: EvaluateUpstreamResponseValidity(), GetDefaultUpstreamResponseNote()
    //
    // Remove: RequestsUsed, RequestsAvailable, UsedPercent, RemainingPercent
    // Remove: PlanType, IsCurrencyUsage, DisplayAsFraction, IsQuotaBased
    // Remove: NextResetTime, ParentProviderId, Name, CardId, GroupId
    // Remove: WindowKind, ModelName, PeriodDuration
    // Remove: IsStatusOnly, IsTooltipOnly, WindowCards, UsagePerHour
}
```

- [ ] **Step 2: Create `QuotaProviderUsage.cs`**

```csharp
namespace AIUsageTracker.Core.Models;

public class QuotaProviderUsage : ProviderUsage
{
    public double RequestsUsed { get; set; }
    public double RequestsAvailable { get; set; }

    [JsonPropertyName("used_percent")]
    public double UsedPercent { get; set; }

    [JsonIgnore]
    public double RemainingPercent => Math.Max(0, 100.0 - UsedPercent);

    public PlanType PlanType { get; set; } = PlanType.Usage;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsCurrencyUsage { get; set; }

    public bool DisplayAsFraction { get; set; }

    public DateTime? NextResetTime { get; set; }
}
```

- [ ] **Step 3: Create `WindowedProviderUsage.cs`**

```csharp
namespace AIUsageTracker.Core.Models;

public sealed class WindowedProviderUsage : QuotaProviderUsage
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public WindowKind WindowKind { get; set; } = WindowKind.None;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CardId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GroupId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentProviderId { get; set; }

    [JsonIgnore]
    public IReadOnlyList<ProviderUsage>? WindowCards { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TimeSpan? PeriodDuration { get; set; }
}
```

- [ ] **Step 4: Create `ModelScopedProviderUsage.cs`**

```csharp
namespace AIUsageTracker.Core.Models;

public sealed class ModelScopedProviderUsage : QuotaProviderUsage
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ModelName { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public WindowKind WindowKind { get; set; } = WindowKind.None;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CardId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GroupId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TimeSpan? PeriodDuration { get; set; }
}
```

- [ ] **Step 5: Create `StatusProviderUsage.cs`**

```csharp
namespace AIUsageTracker.Core.Models;

public sealed class StatusProviderUsage : ProviderUsage
{
    // No additional properties. Only base class fields.
    // The type itself communicates "no usage data available."
}
```

- [ ] **Step 6: Build to verify compilation**

Run: `dotnet build AIUsageTracker.Core/AIUsageTracker.Core.csproj`
Expected: Build succeeds. Warnings about abstract class instantiation in provider implementations (expected — Task 2 handles this).

---

### Task 2: Add typed factory methods to ProviderBase

**Files:**
- Modify: `AIUsageTracker.Core/Providers/ProviderBase.cs`

- [ ] **Step 1: Add typed factory methods**

```csharp
protected static QuotaProviderUsage CreateQuotaUsage(ProviderConfig config)
{
    return new QuotaProviderUsage
    {
        ProviderId = config.ProviderId,
        // ... base fields from config
        PlanType = PlanType.Usage,
    };
}

protected static WindowedProviderUsage CreateWindowedUsage(ProviderConfig config)
{
    return new WindowedProviderUsage
    {
        ProviderId = config.ProviderId,
        PlanType = PlanType.Coding,
    };
}

protected static ModelScopedProviderUsage CreateModelScopedUsage(ProviderConfig config)
{
    return new ModelScopedProviderUsage
    {
        ProviderId = config.ProviderId,
    };
}

protected static StatusProviderUsage CreateStatusUsage(ProviderConfig config, ProviderUsageState state, string description)
{
    return new StatusProviderUsage
    {
        ProviderId = config.ProviderId,
        ProviderName = ProviderDefinitions.GetDisplayName(config.ProviderId),
        IsAvailable = false,
        State = state,
        Description = description,
    };
}
```

- [ ] **Step 2: Refactor existing `CreateUnavailableUsage` helpers to return `StatusProviderUsage`**

```csharp
protected static StatusProviderUsage CreateUnavailableUsage(ProviderConfig config, string description)
{
    return CreateStatusUsage(config, ProviderUsageState.Error, description);
}

protected static StatusProviderUsage CreateUnavailableUsageFromException(ProviderConfig config, Exception ex, string? description = null)
{
    return CreateStatusUsage(config, ProviderUsageState.Error, description ?? ex.Message);
}

// ... refactor all existing CreateUnavailableUsage* methods similarly
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build AIUsageTracker.Core/AIUsageTracker.Core.csproj`
Expected: Build succeeds.

---

### Task 3: Migrate single-window quota providers (10 providers)

**Files:**
- Modify: `AIUsageTracker.Infrastructure/Providers/SyntheticProvider.cs`
- Modify: `AIUsageTracker.Infrastructure/Providers/DeepSeekProvider.cs`
- Modify: `AIUsageTracker.Infrastructure/Providers/MistralProvider.cs`
- Modify: `AIUsageTracker.Infrastructure/Providers/MinimaxProvider.cs`
- Modify: `AIUsageTracker.Infrastructure/Providers/KimiProvider.cs`
- Modify: `AIUsageTracker.Infrastructure/Providers/OpenRouterProvider.cs`
- Modify: `AIUsageTracker.Infrastructure/Providers/XiaomiProvider.cs`
- Modify: `AIUsageTracker.Infrastructure/Providers/ZaiProvider.cs`
- Modify: `AIUsageTracker.Infrastructure/Providers/OpenCodeProvider.cs`
- Modify: `AIUsageTracker.Infrastructure/Providers/OpenCodeZenProvider.cs`
- Test: `dotnet test AIUsageTracker.Monitor.Tests --filter "FullyQualifiedName~SyntheticProvider"` (repeat per provider)

- [ ] **Step 1: Migrate SyntheticProvider**

Switch all `new ProviderUsage { ... }` and `CreateBaseUsage(...)` to `CreateQuotaUsage(...)`. Set subtype-specific fields directly after creation:

```csharp
var usage = CreateQuotaUsage(config);
usage.ProviderName = "Synthetic";
usage.UsedPercent = Math.Min(100, used / total * 100);
usage.RequestsUsed = used;
usage.RequestsAvailable = total;
usage.NextResetTime = parsedReset;
usage.Description = $"{used} / {total} credits (Resets: ...)";
```

- [ ] **Step 2: Build and run SyntheticProvider tests**

Run: `dotnet build && dotnet test AIUsageTracker.Monitor.Tests --filter "SyntheticProvider" --no-build`
Expected: All SyntheticProvider tests pass.

- [ ] **Step 3: Migrate remaining 9 single-window providers** (same pattern, repeat per provider)

Each provider: replace `new ProviderUsage { ... }` with `CreateQuotaUsage(config)`, set fields. Build and test after each.

- [ ] **Step 4: Commit**

```bash
git add AIUsageTracker.Infrastructure/Providers/SyntheticProvider.cs ... (all 10)
git commit -m "refactor: migrate single-window quota providers to QuotaProviderUsage"
```

---

### Task 4: Migrate multi-window providers (OpenAI, GitHub Copilot)

**Files:**
- Modify: `AIUsageTracker.Infrastructure/Providers/OpenAIProvider.cs`
- Modify: `AIUsageTracker.Infrastructure/Providers/GitHubCopilotProvider.cs`

- [ ] **Step 1: Migrate OpenAIProvider**

Switch to `CreateWindowedUsage(config)`. Set `WindowKind`, `CardId`, `GroupId`, `Name`, `PeriodDuration`, `WindowCards` on the result.

- [ ] **Step 2: Build and test OpenAIProvider**

Run: `dotnet build && dotnet test AIUsageTracker.Monitor.Tests --filter "OpenAI" --no-build`
Expected: All pass.

- [ ] **Step 3: Migrate GitHubCopilotProvider**

Switch to `CreateWindowedUsage(config)`. The monthly child card becomes `WindowedProviderUsage` too.

- [ ] **Step 4: Build and test GitHubCopilotProvider**

Run: `dotnet build && dotnet test AIUsageTracker.Monitor.Tests --filter "GitHubCopilot" --no-build`
Expected: All pass.

- [ ] **Step 5: Commit**

```bash
git add AIUsageTracker.Infrastructure/Providers/OpenAIProvider.cs AIUsageTracker.Infrastructure/Providers/GitHubCopilotProvider.cs
git commit -m "refactor: migrate multi-window providers to WindowedProviderUsage"
```

---

### Task 5: Migrate model-scoped providers (Gemini, Codex)

**Files:**
- Modify: `AIUsageTracker.Infrastructure/Providers/GeminiProvider.cs`
- Modify: `AIUsageTracker.Infrastructure/Providers/CodexProvider.cs`

- [ ] **Step 1: Migrate GeminiProvider**

Switch to `CreateModelScopedUsage(config)`. Set `ModelName`, `WindowKind`, `Name`, `CardId`, `GroupId`.

- [ ] **Step 2: Migrate CodexProvider** (same pattern)

- [ ] **Step 3: Build and test**

Run: `dotnet build && dotnet test AIUsageTracker.Monitor.Tests --filter "Gemini|Codex" --no-build`
Expected: All pass.

- [ ] **Step 4: Commit**

```bash
git commit -m "refactor: migrate model-scoped providers to ModelScopedProviderUsage"
```

---

### Task 6: Migrate AntigravityProvider to StatusProviderUsage

**Files:**
- Modify: `AIUsageTracker.Infrastructure/Providers/AntigravityProvider.cs`

- [ ] **Step 1: Switch to StatusProviderUsage**

Replace `new ProviderUsage { ... }` with `CreateStatusUsage(config, ProviderUsageState.Available, "System provider")`.

- [ ] **Step 2: Migrate ClaudeCodeProvider**

ClaudeCode uses `CreateBaseUsage` for its aggregate children. Each child becomes `QuotaProviderUsage` or `StatusProviderUsage` depending on whether it carries quota data.

- [ ] **Step 3: Build and test**

Run: `dotnet build && dotnet test AIUsageTracker.Monitor.Tests --filter "Antigravity|ClaudeCode" --no-build`
Expected: All pass.

- [ ] **Step 4: Commit**

---

### Task 7: Update persistence layer for card_type

**Files:**
- Modify: `AIUsageTracker.Monitor/Services/ProviderUsagePersistenceService.cs`
- Modify: `AIUsageTracker.Monitor/Services/UsageDatabase.cs`

- [ ] **Step 1: Add `card_type` column handling to database writes**

In `StoreHistoryAsync`, determine the card type from the subtype and write it alongside existing columns:

```csharp
string cardType = usage switch
{
    StatusProviderUsage => "status",
    WindowedProviderUsage => "windowed",
    ModelScopedProviderUsage => "model",
    QuotaProviderUsage => "quota",
    _ => null
};
```

- [ ] **Step 2: Add `card_type` column handling to database reads**

In the read path, construct the correct subtype based on `card_type` column value. Fall back to `ProviderUsage` (abstract) → `QuotaProviderUsage` for legacy rows where `card_type` is null.

- [ ] **Step 3: Build and test persistence layer**

Run: `dotnet build && dotnet test AIUsageTracker.Monitor.Tests --filter "Persistence|Database" --no-build`
Expected: All pass.

- [ ] **Step 4: Commit**

---

### Task 8: Migrate Slim UI consumers

**Files:**
- Modify: `AIUsageTracker.UI.Slim/Services/MonitorService.cs`
- Modify: `AIUsageTracker.UI.Slim/ViewModels/MainWindowViewModel.cs`
- Modify: `AIUsageTracker.UI.Slim/Services/ProviderCardRenderer.cs`
- Modify: `AIUsageTracker.UI.Slim/Services/ProviderUsageDisplayCatalog.cs`
- Modify: `AIUsageTracker.UI.Slim/Services/MainWindowRuntimeLogic.cs`
- Modify: `AIUsageTracker.UI.Slim/Services/ProviderStatusPresentationCatalog.cs`
- Modify: `AIUsageTracker.UI.Slim/Services/ProviderResetBadgePresentationCatalog.cs`
- Modify: `AIUsageTracker.UI.Slim/Converters/PercentageToColorConverter.cs`

- [ ] **Step 1: Swap null-checks for pattern matches**

Replace patterns like:
```csharp
if (usage.RequestsAvailable > 0 && usage.UsedPercent > 0)
```

With:
```csharp
if (usage is QuotaProviderUsage q && q.UsedPercent > 0)
```

Or, for type-specific branches:
```csharp
switch (usage)
{
    case WindowedProviderUsage w:
        RenderWindowToggle(w);
        break;
    case QuotaProviderUsage q:
        RenderChart(q);
        break;
    case StatusProviderUsage:
        RenderStatusCard(usage);
        break;
}
```

- [ ] **Step 2: Build Slim UI**

Run: `dotnet build AIUsageTracker.UI.Slim/AIUsageTracker.UI.Slim.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

---

### Task 9: Migrate Web UI consumers

**Files:**
- Modify: `AIUsageTracker.Web/Pages/Dashboard.cshtml.cs`
- Modify: `AIUsageTracker.Web/Pages/Providers.cshtml.cs`
- Modify: `AIUsageTracker.Web/Pages/ProviderDetails.cshtml.cs`
- Modify: `AIUsageTracker.Web/Pages/History.cshtml.cs`
- Modify: `AIUsageTracker.Web/Pages/Index.cshtml`

- [ ] **Step 1: Swap null-checks for pattern matches** (same pattern as Slim UI)

- [ ] **Step 2: Build Web UI**

Run: `dotnet build AIUsageTracker.Web/AIUsageTracker.Web.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

---

### Task 10: Migrate CLI consumer

**Files:**
- Modify: `AIUsageTracker.CLI/Commands/StatusCommand.cs`

- [ ] **Step 1: Swap null-checks for pattern matches**

- [ ] **Step 2: Build CLI**

Run: `dotnet build AIUsageTracker.CLI/AIUsageTracker.CLI.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

---

### Task 11: Cleanup — remove migrated base properties, add guardrails

**Files:**
- Modify: `AIUsageTracker.Core/Models/ProviderUsage.cs`
- Create: `AIUsageTracker.Tests/Architecture/ProviderUsageShapeContractGuardrailTests.cs`

- [ ] **Step 1: Remove subtype-only properties from base class**

Remove from `ProviderUsage`: `RequestsUsed`, `RequestsAvailable`, `UsedPercent`, `RemainingPercent`, `PlanType`, `IsCurrencyUsage`, `DisplayAsFraction`, `IsQuotaBased`, `NextResetTime`, `ParentProviderId`, `Name`, `CardId`, `GroupId`, `WindowKind`, `ModelName`, `PeriodDuration`, `IsStatusOnly`, `IsTooltipOnly`, `WindowCards`, `UsagePerHour`.

- [ ] **Step 2: Build to verify nothing references removed properties from the base**

Run: `dotnet build`
Expected: Build succeeds (any broken references were caught by Tasks 3-10).

- [ ] **Step 3: Add architecture guardrail test**

```csharp
[Fact]
public void ProviderUsage_AbstractClass_HasOnlyBaseProperties()
{
    var props = typeof(ProviderUsage).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
    
    // Only identity, availability, and diagnostics — no quota/usage fields
    Assert.Contains(props, p => p.Name == "ProviderId");
    Assert.Contains(props, p => p.Name == "IsAvailable");
    Assert.Contains(props, p => p.Name == "HttpStatus");
    Assert.DoesNotContain(props, p => p.Name == "RequestsUsed");
    Assert.DoesNotContain(props, p => p.Name == "IsStatusOnly");
}

[Fact]
public void AllProviderUsageSubtypes_AreDiscoveredByJsonDerivedType()
{
    var derivedTypes = typeof(ProviderUsage)
        .GetCustomAttributes<JsonDerivedTypeAttribute>()
        .Select(a => a.DerivedType)
        .ToHashSet();
    
    Assert.Contains(typeof(QuotaProviderUsage), derivedTypes);
    Assert.Contains(typeof(WindowedProviderUsage), derivedTypes);
    Assert.Contains(typeof(ModelScopedProviderUsage), derivedTypes);
    Assert.Contains(typeof(StatusProviderUsage), derivedTypes);
    Assert.Equal(4, derivedTypes.Count);
}
```

- [ ] **Step 4: Run guardrail tests**

Run: `dotnet test AIUsageTracker.Tests/AIUsageTracker.Tests.csproj --filter "ProviderUsageShapeContract" --no-build`
Expected: PASS.

- [ ] **Step 5: Full test suite**

Run: `dotnet test` (or via `scripts/run-local-tests-safe.ps1`)
Expected: All 1363+ tests pass.

- [ ] **Step 6: Commit**
