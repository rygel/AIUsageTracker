# Auth Information Flow

This document describes how provider authentication data flows through the application today, and the intended precedence policy.

## Scope

Applies to:

- `AIUsageTracker.Infrastructure.Configuration.JsonConfigLoader`
- `AIUsageTracker.Infrastructure.Configuration.TokenDiscoveryService`
- `AIUsageTracker.Core.Paths.ConfigPathCatalog`
- Monitor refresh pipeline (`AIUsageTracker.Monitor`)

## Read Flow (Startup / Refresh)

1. Config files are read in the order returned by `ConfigPathCatalog.GetConfigEntries(...)`.
2. Each file is merged into `ProviderConfig` entries by provider id (after canonical id normalization).
3. Token discovery runs after file loading and only fills missing keys.
4. The merged result is normalized and used by provider refresh.

## Current Config Sources and Precedence

Current `ConfigPathCatalog` order:

1. Auth file: `IAppPathProvider.GetAuthFilePath()`
2. Provider file: `IAppPathProvider.GetProviderConfigFilePath()`

With the default path provider this resolves to:

- Auth: `%USERPROFILE%\.opencode\auth.json`
- Provider config: `%LOCALAPPDATA%\AIUsageTracker\providers.json`

## Merge Rules (Important)

In `JsonConfigLoader.ApplyFileConfig(...)`:

- If the current file is marked as `Auth`, key values always override earlier values.
- If the current file is `Provider`, key values are only used when no key is already set.
- Non-key metadata (`type`, `base_url`, tray flags, etc.) is merged from both files.

Implication:

- Auth file ordering controls final key precedence.
- Later auth sources win over earlier auth sources.

## Token Discovery Behavior

`TokenDiscoveryService` runs after file merge:

- Environment variables
- Kilo/Roo/Claude/Codex discovery sources

Discovery does not replace already-populated keys; it fills gaps and adds discovered providers.

## Persistence (Write Flow)

`JsonConfigLoader.SaveConfigAsync(...)` writes:

- Keys to auth file (`GetAuthFilePath`)
- Non-secret provider settings to provider file (`GetProviderConfigFilePath`)

So `providers.json` is not the source of truth for secret keys.

## Policy: "Own Auth File Read Last"

Desired policy for multiple auth files:

1. External/shared auth sources first (for compatibility).
2. App-owned auth source last (highest precedence override).

Reason:

- The app-owned auth file should be the final authority for user-entered keys.
- This allows compatibility imports without overriding explicit in-app credentials.

## Quick Diagnostics

When provider quota data is stale/missing:

1. Check live merged config:
   - `GET http://localhost:5000/api/config`
2. Verify provider key lengths for affected providers.
3. Check monitor logs for `NO API KEY` and circuit-breaker entries:
   - `%LOCALAPPDATA%\AIUsageTracker\logs\monitor_YYYY-MM-DD.log`

