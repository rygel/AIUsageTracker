# ADR-005: Hide Missing-State Cards from Main Window for StandardApiKey Providers

## Status
Accepted — 2026-04-05

## Context

When a user deletes an API key for a StandardApiKey provider (e.g., Synthetic), the provider card persisted in the main window showing "API Key missing." This happened because:

1. The monitor returns a `ProviderUsageState.Missing` usage entry for providers with no configured key.
2. `PrepareForMainWindow` filtered only by `ShowInMainWindow` metadata and `HiddenProviderItemIds` — it had no filter for usage state.
3. The ETag cache in `MonitorService.GetGroupedUsageAsync()` was never invalidated after config changes, so stale data (including the deleted provider) was served for up to 60 seconds.

The card was not actionable in the main window — the user could not fix "API Key missing" from there. It had to be configured in Settings.

Meanwhile, session-based providers (GitHub Copilot, Codex) also use `Missing` state to communicate "Not authenticated," which IS useful information in the main window — it tells the user to log in.

## Decision

### 1. Filter Missing-state cards by provider settings mode

`MainWindowRuntimeLogic.PrepareForMainWindow` now hides usage entries where:
- `usage.State == ProviderUsageState.Missing`, AND
- `definition.SettingsMode == ProviderSettingsMode.StandardApiKey`

Providers with other settings modes (SessionAuthStatus, ExternalAuthStatus, AutoDetectedStatus) continue to show their Missing-state cards, since "Not authenticated" or "Not detected" is actionable context.

Error-state cards (`ProviderUsageState.Error`) are always shown regardless of settings mode, since they indicate a runtime problem worth surfacing.

### 2. Invalidate ETag cache after config changes

`IMonitorService.InvalidateGroupedUsageCache()` was added to clear the cached ETag and snapshot. It is called in `PersistAllSettingsAsync` after saving or removing configs, so the next `GetGroupedUsageAsync()` call fetches fresh data instead of returning a stale 304.

### 3. Settings dialog always shows all provider cards

The Settings dialog is a configuration surface. All providers with `ShowInSettings=true` are always displayed as configuration slots — even if the key is empty. The user can see the Inactive badge and type a new key at any time. No cards are removed from Settings on key deletion.

## Consequences

- Deleting a StandardApiKey provider's key immediately removes its card from the main window and tray.
- Session/external auth providers still show their authentication status in the main window.
- The Settings dialog is unaffected — all provider slots remain visible for reconfiguration.
- The ETag cache invalidation adds negligible overhead (one lock + two null assignments per save cycle).
