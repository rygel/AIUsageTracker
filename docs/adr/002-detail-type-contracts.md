# ADR-002: Provider Detail Type Contracts

## Status
Accepted — 2026-03-15

## Context

`ProviderUsageDetail.DetailType` (`ProviderUsageDetailType`) determines how a detail row is rendered in the Slim UI, Web UI, and CLI. Early code used string heuristics (`"window"` in `Name`, etc.). The typed enum eliminated ambiguity but the rendering paths for each type have evolved and need to be documented explicitly.

## Enum Values and Their Rendering Paths

### `QuotaWindow` (value 1)
**Who sets it:** Providers with discrete quota windows (Codex, Gemini CLI, Kimi, GitHub Copilot).

**Rules:**
- `QuotaBucketKind` must be `Burst`, `Rolling`, or `ModelSpecific` (never `None`). The pipeline rejects `QuotaWindow + WindowKind.None` as a contract error.
- Renders as a dual progress bar on the parent card when two `QuotaWindow` details with distinct `QuotaBucketKind` are present and both have resolvable `PercentageValue`.
- Does **not** appear in `GetDisplayableDetails` (the tooltip sub-detail list) — `IsEligibleDetail` excludes `QuotaWindow`.
- `NextResetTime` on `QuotaWindow` details is used by `ProviderDualQuotaBucketPresentationCatalog` to populate `PrimaryResetTime`/`SecondaryResetTime` on the dual presentation.

### `Model` (value 3)
**Who sets it:** Providers with per-model breakdown (Antigravity, Claude Code, grouped usage models).

**Rules:**
- Appears in `GetDisplayableDetails` (shown as sub-rows in the tooltip).
- For `SyntheticAggregateChildren` providers (Claude Code), `Model`-type details are expanded into individual synthetic child `ProviderUsage` objects by `ProviderUsageDisplayCatalog.ExpandSyntheticAggregateChildren`.
- `QuotaBucketKind` on `Model` details controls sort order within `ExpandSyntheticAggregateChildren` (Burst=0, ModelSpecific=1, Rolling=2) but is **not** used by `TryGetPresentation` (which only reads `QuotaWindow` details).
- `NextResetTime` on a `Model` detail is carried directly to the synthetic child card's `NextResetTime`.

### `RateLimit` (value 5)
**Who sets it:** Providers that expose rate-limit metadata (Claude Code OAuth, OpenAI).

**Rules:**
- Appears in `GetDisplayableDetails` (tooltip sub-rows). Excluded from tray sub-items (`IsEligibleDetail(detail, includeRateLimit: false)`).
- `QuotaBucketKind` should be `None`.

### `Credit` (value 2)
**Who sets it:** Providers with credit/balance rows.

**Rules:**
- Excluded from `GetDisplayableDetails` (not in `IsEligibleDetail`). Rendered via custom credit display paths.

### `Other` (value 4)
**Who sets it:** Supplemental informational rows.

**Rules:**
- Appears in `GetDisplayableDetails` and tray sub-items.

### `Unknown` (value 0)
- Invalid for any provider output. The pipeline converts the entire usage to an error response if any detail has `DetailType=Unknown`.

## WindowKind (QuotaBucketKind)

| Value | Canonical name | Obsolete alias | Meaning |
|---|---|---|---|
| 0 | `None` | — | Non-window detail (Credit/Model/Other/RateLimit) |
| 1 | `Burst` | `Primary` | Short-lived window (5-hour, per-minute) |
| 2 | `Rolling` | `Secondary` | Long-lived rolling window (weekly, daily) |
| 3 | `ModelSpecific` | `Spark` | Model-scoped window (Sonnet/Opus individual quotas) |

The `Primary`/`Secondary`/`Spark` aliases are `[Obsolete]`. Use `Burst`/`Rolling`/`ModelSpecific` in new code.

## PercentageValue vs Used string

Providers should prefer `SetPercentageValue(double, PercentageValueSemantic)` over setting the `Used` string. The typed path is:
- More robust through serialisation/deserialisation.
- Required for dual bar rendering after `NormalizeDetails` processing (the `Used` string is NOT preserved through some database read paths).

The `Used` string is still read as a fallback in `GetEffectiveUsedPercent` and `ParsePercent`, but it should be treated as legacy compatibility only.

## Contract Enforcement

`ProviderUsageProcessingPipeline.TryCreateDetailContractErrorUsage` rejects:
- `DetailType=Unknown`
- `DetailType=QuotaWindow` + `QuotaBucketKind=None`
- Empty `Name`

Violations convert the entire provider usage to an unavailable/error state.

## Consequences

- New providers with quota windows: use `QuotaWindow` + non-`None` `QuotaBucketKind` + `SetPercentageValue`.
- New providers with model breakdowns: use `Model` + `SetPercentageValue` (or `Used` for legacy).
- Test coverage: `ProviderUsageProcessingPipelineTests` (contract error tests + NormalizeDetails field preservation).
- See also: `docs/provider_detail_contract.md` (older overview), `docs/adr/001-reset-time-presentation.md`.
