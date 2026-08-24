# ADR-005: Missing-state cards (superseded)

Originally 2026-04-05. Superseded 2026-04-12.

Do not filter `State == Missing && StandardApiKey` in `PrepareForMainWindow`. Filtering belongs in the Monitor:

1. `ProviderRefreshService.SelectActiveRefreshConfigs` skips `StandardApiKey` providers with an empty key, even when `forceAll`.
2. Grouped usage calls `GetLatestHistoryAsync(visibleIds)` where StandardApiKey needs a key (`MonitorUsageEndpoints.MapGetGroupedUsage`).
3. Settings lists `ShowInSettings=true` (`SettingsWindow.Providers.cs`).
4. After config save/remove, call `IMonitorService.InvalidateGroupedUsageCache`.

Session/external providers can still show Missing in the main window. History is not deleted on key removal.
