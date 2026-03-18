# Changelog

## [Unreleased]

## [2.3.1-beta.2] - 2026-03-18

### Fixed
- **OpenAI Codex data no longer updating**: Codex usage stopped refreshing on 2026-03-14 due to a change in the OpenAI API response format. The parser now handles the new response shape correctly. If your Codex card has been showing the same data for days, it will update automatically on the next refresh cycle.
- **Stale data shown silently after re-authentication**: When a provider's auth token expires or is missing, the app now stores a visible "re-authenticate" message instead of continuing to show old cached data. After a database wipe or first run, you will see an actionable message rather than an empty card.
- **Stale data indicator**: Provider cards that have not refreshed in over an hour now show a "last refreshed X ago — data may be outdated" notice, so you always know when the data is fresh versus cached.
- **Circuit-breaker providers hidden from UI**: When a provider is temporarily paused due to repeated failures, the UI now shows a "Temporarily paused — next check at HH:MM" message instead of silently serving stale cached data or showing nothing. The pause duration and last error are included so you know when it will retry.
- **Config and startup errors now visible in health endpoint**: Failures during config loading or startup (e.g. a corrupted config file) are now reported in the monitor health endpoint instead of being silently swallowed.
- **Connectivity check returned misleading 404**: The provider connectivity check endpoint now returns 503 with a clear message when no usage data is available, rather than a 404 that implied the endpoint itself was missing.

## [2.3.1-beta.1] - 2026-03-18

### Removed
- **AnthropicProvider**: Removed the non-functional stub provider that returned hardcoded responses with no real API integration.

### Changed
- **ProviderBase Helpers**: Added `CreateBearerRequest()` and `DeserializeJsonOrDefault<T>()` helpers to `ProviderBase`, eliminating repeated boilerplate across all provider implementations.

### CI/CD
- Updated all GitHub Actions to latest major versions (checkout v6, setup-dotnet v5, upload-artifact v7, download-artifact v8, github-script v8, cache v5, codecov v5, create-pull-request v8, paths-filter v4) to eliminate Node.js 20 deprecation warnings.

