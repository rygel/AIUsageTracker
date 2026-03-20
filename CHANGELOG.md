# Changelog

## [Unreleased]

## [2.3.2-beta.4] - 2026-03-20

### Fixed
- **Quota-based provider cards permanently stuck yellow/red**: `PercentageToColorConverter` was computing `remaining = 100 − usedPercent` for quota-based providers and then checking `remaining ≥ yellowThreshold`. With default thresholds (yellow = 60, red = 80), any provider with more than 40% remaining was shown as yellow or red regardless of actual usage — e.g. 30% used → 70% remaining → 70 ≥ 60 → yellow. The fix removes the inversion: colour thresholds now always compare against the used percentage (pace-adjusted for rolling-window providers), consistent with the threshold labels in Settings. Affects all quota-based providers: Claude Code, GitHub Copilot, OpenAI Codex, Z.AI, Opencode Zen, and others.

### Security
- **Resolved all 64 GitHub code-scanning alerts** (zizmor): `dangerous-triggers` — added trusted-source repository guard to the `workflow_run` trigger in `build-performance-monitor.yml`; `artipacked` — added `persist-credentials: false` to checkout steps in `release.yml` and `dependency-updates.yml`; `excessive-permissions` — added `permissions: contents: read` at workflow level in `experimental-rust.yml`; `template-injection` — moved all inline `${{ }}` expressions in shell scripts and `github-script` blocks to `env:` vars across `tests.yml`, `build-performance-monitor.yml`, and `dependency-updates.yml`.

## [2.3.2-beta.3] - 2026-03-20

### Fixed
- **Pace adjustment not applied to Claude Code Sonnet/Opus cards**: The rolling-window pace indicator was correctly applied to the primary Claude Code card but not to the per-model Sonnet and Opus detail cards. All three cards now reflect pace-adjusted colours and thresholds consistently.

### Refactored
- **Provider metadata moved to `ProviderDefinition` (single source of truth)**: Static characteristics (`IsStatusOnly`, `IsCurrencyUsage`, `DisplayAsFraction`, `PlanType`, `IsQuotaBased`, `ProviderName`) are now declared once on `ProviderDefinition`. Per-instance assignments on `ProviderUsage` have been removed. The processing pipeline enforces definition values at the boundary — `PlanType` and `IsQuotaBased` are now overridden from the definition on every normalisation pass, not just at provider construction time.
- **`ProviderBase.CreateUnavailableUsage` sources metadata from definition**: Previously defaulted to `PlanType.Coding` and `IsQuotaBased = true` for all error paths, producing incorrect metadata on error cards for providers like Mistral and DeepSeek.
- **Eliminated post-fetch filtering in GeminiProvider**: Error results from failed account fetches are no longer added to the results list and then filtered out; only successful fetches enter the list. If all accounts fail a single unavailable card is returned.
- **GeminiProvider OAuth client retry made explicit**: The implicit `catch when clientId == CLI` silent retry is replaced by `ResolveOAuthClientsToTry()` + `DetectPreferredOAuthClientId()` — the fallback order is now declared upfront.
- **`ProviderUsageDisplayCatalog` single-pass filtering**: The two sequential filter passes (`ShouldShowInMainWindow` + hidden items) are merged into one `.Where()` predicate.
- **`ResolveDisplayLabel` simplified**: Collapsed from a 7-level fallback chain to 3 clear branches with documented precedence.

## [2.3.2-beta.2] - 2026-03-20

### Fixed
- **Pace adjustment not applied to Claude Code Sonnet/Opus cards**: The rolling-window pace indicator was correctly applied to the primary Claude Code card but not to the per-model Sonnet and Opus detail cards. All three cards now reflect pace-adjusted colours and thresholds consistently.

### Refactored
- **Provider metadata moved to `ProviderDefinition`**: Static characteristics (`IsStatusOnly`, `IsCurrencyUsage`, `DisplayAsFraction`, `PlanType`, `IsQuotaBased`, `ProviderName`) are now declared once on `ProviderDefinition` and sourced from there at runtime. Per-instance assignments on `ProviderUsage` objects have been removed. The processing pipeline ORs in definition flags so that any provider declaring a flag gets it applied even if the runtime object omits it.
- **`ProviderBase.CreateUnavailableUsage` now sources metadata from definition**: The helper previously defaulted to `PlanType.Coding` and `IsQuotaBased = true` for all error/unavailable paths regardless of the actual provider type, causing incorrect metadata on error cards for providers like Mistral and DeepSeek.

## [2.3.2-beta.1] - 2026-03-20

### Added
- **Pace-aware quota colours**: For providers with rolling quota windows (Claude Code 7-day, GitHub Copilot weekly, OpenAI/Codex weekly), the progress-bar colour and notification threshold now account for how much of the quota period has elapsed. A provider at 70% usage with only one day left of a 7-day window is considered on-budget and stays green — not yellow or red — because consumption is below the expected pace. An **"On pace"** badge appears in green when usage is meaningfully under pace. Can be toggled off under **Settings → Layout → Pace-Aware Quota Colours** (enabled by default).
- **Usage rate badge**: Provider cards can now show a live **req/hr** burn-rate badge derived from history without any extra API calls. Toggle it on under **Settings → Layout → Show Usage Rate (req/hr)** (off by default). The badge is hidden when fewer than 30 minutes of history exist or after a quota reset.

### Fixed
- **HTTP 429 rate-limit cards now show orange instead of red**: A rate-limited provider is a temporary state, not a configuration error. Cards now render with a Warning (orange) tone so you know to wait rather than investigate.
- **Monitor offline status now shows relative time**: The status bar shows `"Monitor offline — last sync 7m ago"` instead of a generic "Connection lost" message or an absolute timestamp.
- **Stale-detection failures on non-UTC machines**: `fetched_at` timestamps in `provider_history` and `raw_snapshots` are now stored as Unix epoch integers instead of ISO-8601 text strings, eliminating `DateTimeKind.Unspecified` comparison failures that caused the stale-data indicator to fire incorrectly on machines not set to UTC.

### Security
- **SQL injection hardening**: Table names and ORDER BY clauses in `WebDatabaseRawTableReader` are now validated against an allowlist; LIMIT parameters are bounds-checked (1–10,000). An architecture guardrail test (`ProductionCode_DoesNotUseUnguardedSqlInterpolation`) enforces this going forward.
- **Added CodeQL, Semgrep, and Trivy security scanning**: Three complementary scanners now run on a sensible schedule — Semgrep and Trivy secret-scanning gate every PR (fast, pattern-based), while CodeQL deep semantic analysis and Trivy full vulnerability scanning run weekly. Results are uploaded to GitHub Security as SARIF.

### CI/CD
- Screenshot baseline comparison now runs on `windows-2025` for both generation and comparison, eliminating false failures caused by WPF font rendering differences between Windows 10 and Windows Server 2025.

## [2.3.1] - 2026-03-19

### Fixed
- **OpenAI Codex data no longer updating**: Codex usage stopped refreshing on 2026-03-14 due to a change in the OpenAI API response format. The parser now handles the new response shape correctly.
- **Stale data shown silently after re-authentication**: When a provider's auth token expires or is missing, the app now stores a visible "re-authenticate" message instead of continuing to show old cached data.
- **Stale data indicator**: Provider cards that have not refreshed in over an hour now show a "last refreshed X ago — data may be outdated" notice.
- **Circuit-breaker providers hidden from UI**: When a provider is temporarily paused due to repeated failures, the UI now shows a "Temporarily paused — next check at HH:MM" message with the pause duration and last error.
- **Config and startup errors now visible in health endpoint**: Failures during config loading or startup are now reported in the monitor health endpoint instead of being silently swallowed.
- **Connectivity check returned misleading 404**: The provider connectivity check endpoint now returns 503 with a clear message when no usage data is available.

### Changed
- **`provider_history` write deduplication**: The database no longer stores rows when nothing has changed. Only `fetched_at` is updated on the existing row so the stale-data detector stays accurate. During idle periods this eliminates virtually all writes.
- **Automatic `provider_history` compaction**: Once per day, old history rows are downsampled — rows 7–90 days old to one per hour, rows older than 90 days to one per day. A `VACUUM` follows to reclaim freed disk space.
- **Monitor version logged on startup**: The monitor now logs its full version (including pre-release suffix) on every startup, making it easy to confirm from log files which build is running.

## [2.3.1-beta.4] - 2026-03-19

### Added
- **Integration tests for database read paths and pipeline**: Added 28 real-SQLite integration tests covering `GetHistoryAsync`, `GetHistoryByProviderAsync`, `GetRecentHistoryAsync`, and `GetLatestHistoryAsync` — verifying Dapper type mapping, stale-data detection, the full provider-data-to-database pipeline, and circuit-breaker deduplication behaviour. These tests would have caught the beta.3 `Int64`/`Int32` crash before release.

## [2.3.1-beta.3] - 2026-03-19

### Changed
- **`provider_history` write deduplication**: The database no longer stores rows when nothing has changed. Before each write, the last stored row for each provider is checked against the incoming data. If the quota numbers, availability, status message, reset time, and sub-quota details are all identical, no new row is inserted — instead, only `fetched_at` is updated on the existing row so the stale-data detector stays accurate. During idle periods this eliminates virtually all writes; only genuine state changes are recorded.
- **Automatic `provider_history` compaction**: Once per day, old history rows are downsampled: rows 7–90 days old are reduced to one per hour per provider; rows older than 90 days are reduced to one per day. A `VACUUM` runs afterwards to reclaim freed disk space. This acts as a safety valve for intensive-use periods where data changes on every poll.

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

