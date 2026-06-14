# ProviderUsage Shape Contract — Design Spec

**Date:** 2026-06-14
**Status:** Draft
**Motivation:** `ProviderUsage` is a 36-property superset class with no shape contract. Each of 14 providers populates different subsets, forcing consumers into null-check gymnastics and making illegal states representable. This spec replaces the big-bag model with a sealed subtype hierarchy.

---

## 1. Type Hierarchy

`ProviderUsage` becomes `abstract` with ~12 universal properties. Four sealed subtypes branch off it, each carrying only the fields relevant to its shape.

### Base class — abstract `ProviderUsage`

| Category | Properties |
|----------|-----------|
| Identity | `ProviderId`, `ProviderName`, `AccountName`, `ConfigKey`, `AuthSource` |
| Availability | `IsAvailable`, `State`, `Description`, `FetchedAt`, `IsStale` |
| Diagnostics | `HttpStatus`, `ResponseLatencyMs`, `RawJson`, `FailureContext`, `UpstreamResponseValidity`, `UpstreamResponseNote` |

Method `EvaluateUpstreamResponseValidity()` stays on the base — it uses only base-level fields.

### Hierarchy

```
ProviderUsage (abstract)
├── StatusProviderUsage (sealed)      — no usage data
└── QuotaProviderUsage                — has quota fields
    ├── WindowedProviderUsage (sealed)   — adds window fields
    └── ModelScopedProviderUsage (sealed) — adds model fields
```

**`QuotaProviderUsage`** — single-card quota provider (Synthetic, DeepSeek, Mistral, Minimax, Kimi, OpenRouter, Xiaomi, ZAI, OpenCode, OpenCodeZen, ClaudeCode). Not sealed — acts as base for windowed and model-scoped variants.
- `RequestsUsed`, `RequestsAvailable`, `UsedPercent`
- `NextResetTime`
- `PlanType`, `IsCurrencyUsage`, `DisplayAsFraction`
- `IsQuotaBased=true` (implicit)

**`WindowedProviderUsage`** — sealed, multi-window quota provider (OpenAI, GitHub Copilot). Inherits from `QuotaProviderUsage`.
- All quota fields from parent
- `WindowKind`, `PeriodDuration`
- `Name`, `CardId`, `GroupId`
- `WindowCards` — companion child cards

**`ModelScopedProviderUsage`** — sealed, per-model scoped cards (Gemini, Codex). Inherits from `QuotaProviderUsage`.
- All quota fields from parent
- `ModelName`, `WindowKind`
- `Name`, `CardId`, `GroupId`
- `IsQuotaBased` — bool, can be true or false per model (e.g., free tier vs paid)

**`StatusProviderUsage`** — sealed, no usage data (missing keys, error states, pending). Only base properties.
- `IsStatusOnly` is eliminated — the type itself communicates absence of usage data.
- `IsCurrencyUsage`, `DisplayAsFraction` are absent — not applicable.

### Consumer pattern matching

`is QuotaProviderUsage` catches all quota-having types (including `WindowedProviderUsage` and `ModelScopedProviderUsage`).
`is WindowedProviderUsage` catches only multi-window providers.
`is ModelScopedProviderUsage` catches only per-model scoped cards.
`is StatusProviderUsage` catches status-only entries.

### Eliminated and retained bools

| Bool | Action | Reason |
|------|--------|--------|
| `IsStatusOnly` | **Removed** | Replaced by `StatusProviderUsage` subtype — the type communicates absence of usage data |
| `IsCurrencyUsage` | **Retained** on `QuotaProviderUsage` | Modifier on quota-like data, not a shape discriminant. Controls presentation (cost vs credit display) |
| `DisplayAsFraction` | **Retained** on `QuotaProviderUsage` | Same — controls presentation format, not data shape |

---

## 2. Serialization & Wire Contract

Uses .NET 10's `[JsonDerivedType]` (available since .NET 7) for polymorphic JSON:

```csharp
[JsonDerivedType(typeof(QuotaProviderUsage), typeDiscriminator: "quota")]
[JsonDerivedType(typeof(WindowedProviderUsage), typeDiscriminator: "windowed")]
[JsonDerivedType(typeof(ModelScopedProviderUsage), typeDiscriminator: "model")]
[JsonDerivedType(typeof(StatusProviderUsage), typeDiscriminator: "status")]
public abstract class ProviderUsage { ... }
```

Monitor serializes `List<ProviderUsage>` — each item gets a `$type` discriminator field. All consumers (Slim UI, Web UI, CLI) deserialize with the same `JsonSerializerOptions` and get concrete subtypes automatically. No manual discriminator parsing.

### Database layer

Add column `card_type VARCHAR(16)` with values: `quota`, `windowed`, `model`, `status`. Nullable — backfilled during migration. Existing numeric columns (`requests_used`, `requests_available`, etc.) remain in the schema; they store nulls for `StatusProviderUsage` rows.

On read, `UsageDatabase.StoreHistoryAsync` constructs the correct subtype from `card_type` + populated columns.

---

## 3. Factory Methods on `ProviderBase`

Replace the generic `CreateBaseUsage()` with typed factories:

```csharp
protected static QuotaProviderUsage CreateQuotaUsage(ProviderConfig config) { ... }
protected static WindowedProviderUsage CreateWindowedUsage(ProviderConfig config) { ... }
protected static ModelScopedProviderUsage CreateModelScopedUsage(ProviderConfig config) { ... }
protected static StatusProviderUsage CreateStatusUsage(ProviderConfig config, ProviderUsageState state, string description) { ... }
```

Existing helpers (`CreateUnavailableUsage`, `CreateUnavailableUsageFromException`, etc.) are refactored to return `StatusProviderUsage`.

---

## 4. Consumer Pattern

Consumers pattern-match on the subtype instead of null-checking:

```csharp
// Before
if (usage.RequestsAvailable > 0)
{
    RenderChart(usage);
}

// After
if (usage is QuotaProviderUsage q)
{
    RenderChart(q);
}
```

**Quota chart component** checks `is QuotaProviderUsage` (covers `QuotaProviderUsage`, `WindowedProviderUsage` — which inherits from it). Renders chart, or renders nothing if the match fails. Naturally correct.

**Window toggle component** checks `is WindowedProviderUsage`. Renders toggle, or renders nothing. Naturally correct.

**Provider card dispatcher** pattern-matches the subtype to choose the right template. Exhaustive — the compiler warns about unhandled subtypes if a new one is added.

No defensive branches, no fallbacks. The type hierarchy *is* the routing.

---

## 5. Migration Plan (4 Phases)

### Phase A — Type hierarchy (1 commit)
1. Add `[JsonDerivedType]` attributes to `ProviderUsage`
2. Add 4 sealed subclasses (same file or sibling files)
3. Add typed factory methods to `ProviderBase`
4. No consumer changes yet — subclasses are unused
5. Existing tests continue to pass unchanged

### Phase B — Provider migration (14 commits, parallelizable)
Each provider switches from `new ProviderUsage { ... }` or `CreateBaseUsage(...)` to the typed factory method:

| Provider | Target subtype |
|----------|---------------|
| Synthetic | `QuotaProviderUsage` |
| DeepSeek | `QuotaProviderUsage` |
| Mistral | `QuotaProviderUsage` |
| Minimax | `QuotaProviderUsage` |
| Kimi | `QuotaProviderUsage` |
| OpenRouter | `QuotaProviderUsage` |
| Xiaomi | `QuotaProviderUsage` |
| ZAI | `QuotaProviderUsage` |
| OpenCode | `QuotaProviderUsage` |
| OpenCodeZen | `QuotaProviderUsage` |
| ClaudeCode | `QuotaProviderUsage` (synthetic aggregate children are individual `QuotaProviderUsage` or `StatusProviderUsage` cards) |
| OpenAI | `WindowedProviderUsage` |
| GitHubCopilot | `WindowedProviderUsage` |
| Gemini | `ModelScopedProviderUsage` |
| Codex | `ModelScopedProviderUsage` |
| Antigravity | `StatusProviderUsage` (system-only, never has usage data) |

### Phase C — Consumer migration (1 commit per consumer project)
- Slim UI: swap null-checks for pattern matches in `MainWindowViewModel.cs`, `ProviderCardRenderer.cs`, `ProviderUsageDisplayCatalog.cs`, etc.
- Web UI: swap null-checks in `Index.cshtml`, `Dashboard.cshtml.cs`
- CLI: swap null-checks in `StatusCommand.cs`
- Tests: update assertions that reference removed base properties

### Phase D — Cleanup (1 commit)
1. Remove `IsStatusOnly` from base class
2. Remove `ParentProviderId`, `CardId`, `GroupId`, `Name`, `ModelName`, `PeriodDuration`, `WindowKind` from base — each belongs only on its subtype
3. Add `card_type` column to DB schema (nullable, backfill from provider ID)
4. Add architecture guardrail test ensuring no consumer references removed base properties

---

## 6. Testing Strategy

| Phase | Test coverage |
|-------|--------------|
| A | Unit tests for new factory methods on `ProviderBase` |
| B | Existing provider tests exercise the same factory methods — pass unchanged |
| C | Consumer tests exercise pattern-match code paths |
| D | Architecture guardrail ensures removed base properties aren't referenced |
| All | Full 1363-test regression suite runs green after each commit |

No new test infrastructure. The existing suite is the regression harness.

---

## 7. Open Questions

None. All design decisions confirmed during the brainstorming discussion.
