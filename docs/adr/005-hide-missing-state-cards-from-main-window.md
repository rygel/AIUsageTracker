# ADR-005: Hide Missing-State Cards from Main Window for StandardApiKey Providers

## Status
Superseded by refactor on 2026-04-12 — see **Superseding approach** below.

Originally accepted — 2026-04-05.

## Context

When a user deletes an API key for a StandardApiKey provider (e.g., Synthetic), the provider card persisted in the main window showing "API Key missing." This happened because:

1. The monitor returns a `ProviderUsageState.Missing` usage entry for providers with no configured key.
2. `PrepareForMainWindow` filtered only by `ShowInMainWindow` metadata and `HiddenProviderItemIds` — it had no filter for usage state.
3. The ETag cache in `MonitorService.GetGroupedUsageAsync()` was never invalidated after config changes, so stale data (including the deleted provider) was served for up to 60 seconds.

The card was not actionable in the main window — the user could not fix "API Key missing" from there. It had to be configured in Settings.

Meanwhile, session-based providers (GitHub Copilot, Codex) also use `Missing` state to communicate "Not authenticated," which IS useful information in the main window — it tells the user to log in.

## Original Decision (superseded)

### 1. Filter Missing-state cards by provider settings mode

`MainWindowRuntimeLogic.PrepareForMainWindow` hid usage entries where:
- `usage.State == ProviderUsageState.Missing`, AND
- `definition.SettingsMode == ProviderSettingsMode.StandardApiKey`

### 2. Invalidate ETag cache after config changes

`IMonitorService.InvalidateGroupedUsageCache()` was added to clear the cached ETag and snapshot after saves/removals.

### 3. Settings dialog always shows all provider cards

The Settings dialog is a configuration surface. All providers with `ShowInSettings=true` are always displayed as configuration slots — even if the key is empty.

## Problem with the original approach

The `PrepareForMainWindow` filter was a compensating workaround for two upstream defects:

1. `ProviderRefreshConfigSelector` used `forceAll=true` to bypass key checks for all provider types, so StandardApiKey providers without keys were polled and wrote `State=Missing` rows to the history DB.
2. `CachedGroupedUsageProjectionService` used a two-step canonical-ID filter that incorrectly hid entire provider families (e.g. `minimax-io` was hidden because `minimax`, its canonical sibling, had no key).

These produced two redundant filter layers with overlapping logic and subtle bugs.

## Superseding approach

The root cause is fixed at each responsible layer; no compensating filter is needed in the UI:

### 1. `ProviderRefreshConfigSelector` — never poll without a key

StandardApiKey providers are excluded from polling if their key is empty, regardless of `forceAll`. There is nothing useful to fetch without a key, so no `State=Missing` rows are ever written to the DB.

### 2. `IUsageDatabase.GetLatestHistoryAsync(providerIds)` — filter at the SQL level

`CachedGroupedUsageProjectionService` computes a `visibleIds` set (StandardApiKey providers require a key; non-StandardApiKey providers are always included) and passes it directly to the DB. The SQL query adds `AND provider_id IN (...)` to the 24-hour subquery so only relevant rows are fetched. No application-level `Where` clause follows.

### 3. `PrepareForMainWindow` — no state filter

The `State == Missing && StandardApiKey` check has been removed. The UI renders whatever the monitor snapshot contains; filtering is the monitor's responsibility.

### 4. Settings dialog — unchanged

All providers with `ShowInSettings=true` remain visible as configuration slots regardless of key state.

## Consequences

- Deleting a StandardApiKey provider's key immediately removes its card from the main window (next refresh cycle).
- Session/external auth providers still show their authentication status in the main window.
- The Settings dialog is unaffected — all provider slots remain visible for reconfiguration.
- Historical usage data is preserved on key removal; only live monitoring stops.
