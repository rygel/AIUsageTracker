# ADR-003: Synthetic Aggregate Children (SyntheticAggregateChildren Family Mode)

## Status
Accepted — 2026-03-15

## Context

Some providers (currently: Claude Code) return multiple quota windows as `ProviderUsageDetail` rows instead of real sub-providers. The UI needs to display each window as a separate card (like a real derived provider), but the monitor never fetches separate endpoints for them — they all come from one API response.

The `ProviderFamilyMode.SyntheticAggregateChildren` pattern was introduced to handle this without requiring monitor changes per quota window.

## Decision

### Provider definition
Set `familyMode: ProviderFamilyMode.SyntheticAggregateChildren` in the `ProviderDefinition`. Also declare `mainWindowVisibilityItems` for each expected child ID so the settings window can show per-window hide toggles.

### Detail → child card mapping
`ProviderUsageDisplayCatalog.ExpandSyntheticAggregateChildren` performs the expansion at render time (UI layer only, not stored):

1. Detects all root `ProviderUsage` objects whose canonical `ProviderId` resolves to a `SyntheticAggregateChildren` provider.
2. Replaces the parent with synthetic child `ProviderUsage` objects, one per `DetailType=Model` detail.
3. Child `ProviderId` = `"{canonical}.{detail.Name.ToLower().Replace(" ", "-")}"`.
4. Child `NextResetTime` = `detail.NextResetTime` (the window-specific reset time).
5. Child `IsQuotaBased`, `PlanType` from catalog (via `TryGetUsageSemantics`) or parent.

The parent itself is **not** yielded — it is completely replaced by children.

### isAggregateParent guard in ProviderCardPresentationCatalog
A synthetic child card's `ProviderId` resolves to the parent's canonical ID via `GetCanonicalProviderId` (prefix matching). This would incorrectly set `isAggregateParent=true` for children, causing their progress bar to be suppressed.

Fix (since beta.39): `isAggregateParent` is only `true` when `providerId == canonicalProviderId` (exact match, not just prefix resolution). Children never satisfy this condition.

### Dual bars on children
Synthetic children have no `Details`, so `TryGetPresentation` always returns `false` for them. Each child card renders as a single-bar quota card using its own `RequestsPercentage` / `UsedPercent`.

The **parent** card (which exists in the monitor database) also would not show dual bars because Claude Code's details use `DetailType=Model`, not `DetailType=QuotaWindow`. The parent is replaced by children before rendering anyway.

### Settings visibility
The `mainWindowVisibilityItems` list on the `ProviderDefinition` controls which children appear in the Settings "Show/Hide" list. Each entry is `(childProviderId, displayLabel)`. Items hidden by the user are filtered in `ExpandSyntheticAggregateChildren` via the `hiddenItemIds` parameter.

## Consequences

- Adding a new quota window to a `SyntheticAggregateChildren` provider requires:
  1. Emitting a new `DetailType=Model` detail in the provider's response parser.
  2. Adding the child ID to `mainWindowVisibilityItems` in the `ProviderDefinition`.
  3. No monitor, database, or API changes needed.
- Removing a quota window: the child simply won't be created if the detail is absent. No migration needed.
- Each child's reset time comes only from `detail.NextResetTime`. Providers must populate it.
- Test coverage: `ProviderUsageDisplayCatalogTests` (`ExpandSyntheticAggregateChildren_*` tests), `ProviderCardPresentationCatalogTests` (`Create_ShowsProgress_ForSyntheticAggregateChildCard`).
- See also: `docs/adr/001-reset-time-presentation.md`, `docs/adr/002-detail-type-contracts.md`.
