# ADR-001: Reset Time Presentation Architecture

## Status
Accepted — 2026-03-15

## Context

Providers can have multiple quota windows, each with its own reset time (e.g. Claude Code has a 5-hour burst reset and a 7-day rolling reset). The UI has several places that display "time until reset" and they have different capabilities:

| UI element | Location | Can show multiple times? |
|---|---|---|
| Card reset badge | Main window, inline below the bar | Yes — `(1h 23m | 6d 14h)` |
| Sub-detail reset text | Tooltip on card, per-detail row | One per row |
| Status panel "Next reset:" | Settings hover / provider status tab | No — single value only |

Early versions showed no dual reset times anywhere. A regression in `NormalizeDetails` (fixed in beta.39) dropped `PercentageValue` during pipeline normalisation, which caused `TryGetPresentation` to always fail, leaving the card reset badge showing only the nearest single reset time.

## Decision

### Card reset badge (main window)
`ProviderResetBadgePresentationCatalog.ResolveResetTimes` is the single entry point. It:
1. Calls `ProviderDualQuotaBucketPresentationCatalog.TryGetPresentation` first.
2. If dual presentation is available, returns `[PrimaryResetTime, SecondaryResetTime]` (filtered for non-null, distinct).
3. If `suppressSingleResetFallback=true` and no dual presentation: returns empty (the badge is hidden).
4. Otherwise falls back to `[usage.NextResetTime]`.

When the card has `SuppressSingleResetTime=true` (set whenever dual bars render), the fallback is suppressed so the badge shows both dual times or nothing — never a stale single-window time.

### Dual bucket presentation prerequisite
`TryGetPresentation` requires:
- At least two `DetailType=QuotaWindow` details with distinct `QuotaBucketKind` values (not `None`).
- `GetEffectiveUsedPercent` must return a non-null value for each selected bucket.

The typed `PercentageValue` field (set via `SetPercentageValue`) is the primary path. The legacy `Used` string is a fallback. Providers that use the typed path **must** ensure `NormalizeDetails` (in `ProviderUsageProcessingPipeline`) copies `PercentageValue` and `PercentageSemantic` — this is regression-tested in `ProviderUsageProcessingPipelineTests`.

### usage.NextResetTime
Providers set `usage.NextResetTime` on the root `ProviderUsage` object. By convention this is the **nearest** (soonest) reset time — typically the burst window. The pipeline overwrites it with `InferResetTimeFromDetails` only when the provider leaves it null.

`usage.NextResetTime` is NOT the right field to use for displaying multiple reset times.

### Status panel ("Next reset:")
`ProviderStatusPresentationCatalog` adds a single "Next reset: ..." line from `usage.NextResetTime`. For providers with dual windows this always shows the burst reset time only. This is a known limitation; it was not addressed because the status panel is compact and the dual reset times are already visible in the main window badge.

### Synthetic aggregate children (e.g. Claude Code)
Claude Code renders each quota window as a separate synthetic child card (`claude-code.current-session`, `claude-code.all-models`, etc.). Each child card has `NextResetTime` copied from its source `ProviderUsageDetail`. These child cards have no sub-details and no dual bars — their single `NextResetTime` IS the correct window-specific reset time.

## Consequences

- New providers with dual quotas must use `DetailType=QuotaWindow` + distinct `QuotaBucketKind` + `SetPercentageValue` to get dual bars and dual reset times in the badge.
- The status panel will always show only one reset time (limitation, not a bug).
- `NormalizeDetails` must copy all six detail fields: `Name`, `Used`, `Description`, `NextResetTime`, `DetailType`, `QuotaBucketKind`, `PercentageValue`, `PercentageSemantic`, `IsStale`.
- Test coverage: `ProviderResetBadgePresentationCatalogTests`, `ProviderUsageProcessingPipelineTests`.
