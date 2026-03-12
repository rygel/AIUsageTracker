# Provider Detail Contract

This document defines the typed contract for provider usage details across Infrastructure, Monitor, Slim UI, Web UI, and CLI.

## Shared model

The shared model is `ProviderUsageDetail` in `AIUsageTracker.Core`:

- `DetailType` (`ProviderUsageDetailType`)
- `WindowKind` (`WindowKind`)
- `Name`, `Used`, `Description`, `NextResetTime`

Providers are the source of truth for these typed fields.

## DetailType

- `QuotaWindow`: quota window rows (5-hour/weekly/spark/etc.)
- `Credit`: credit/balance rows
- `Model`: model-specific rows
- `Other`: supplemental rows
- `Unknown`: invalid for emitted provider data

## WindowKind

- `Primary`: main quota window
- `Secondary`: secondary quota window
- `Spark`: short-lived/ephemeral window
- `None`: non-window rows (`Credit`, `Model`, `Other`)

## Contract rules

1. Providers must set `DetailType` for every emitted detail.
2. `DetailType=Unknown` is invalid provider output.
3. `DetailType=QuotaWindow` requires `WindowKind != None`.
4. Non-window detail types should use `WindowKind=None`.
5. `Name` must be non-empty for display rows.

Monitor enforces this in `ProviderRefreshService.ValidateDetailContract()`. Invalid detail payloads are converted to unavailable provider results with an explicit error description.

## Client usage rules

Clients (Slim UI, Web UI, CLI) must use typed semantics only:

- Primary quota: `detail.IsPrimaryQuotaDetail()`
- Secondary quota: `detail.IsSecondaryQuotaDetail()`
- Displayable sub-details: `detail.IsDisplayableSubProviderDetail()`

String heuristics such as checking for `"window"`/`"credit"` in `Name` must not be used.

## Slim UI presentation contract (Antigravity)

For Antigravity model rows rendered in Slim UI:

1. Label format must be: `<Model Name> [Antigravity]`.
2. The parent Antigravity `AccountName` must be propagated to model child rows.
3. Username rendering must respect privacy mode (masked when privacy mode is enabled).
4. Username visual treatment should be secondary (non-bold, italic) to keep model name primary.

## Migration posture

- Existing historical records are preserved.
- No runtime backfill heuristics are applied to legacy untyped rows.
- New provider refreshes are expected to emit typed detail rows.
