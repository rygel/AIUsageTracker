# ADR-001: Reset time presentation

Accepted 2026-03-15. Current implementation: `ProviderCardPresentation.SuppressSingleResetTime` (`MainWindowRuntimeLogic.Presentation`).

Dual-bar cards (`TryBuildDualBarData`) need a `WindowedProviderUsage` with both `WindowKind.Burst` and `WindowKind.Rolling` window cards plus matching `QuotaWindows` on the definition. Dual-bar status sets `SuppressSingleResetTime` so a stale single time is not shown (`CheckboxCardOutputTests.DualQuotaBars_Present_SuppressesSingleResetTime`).

`usage.NextResetTime` is the nearest reset when a single badge is shown.
