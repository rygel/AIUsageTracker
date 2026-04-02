# Changelog

## [Unreleased]

## [2.3.4-beta.20] - 2026-04-01

### Fixed
- **Clearing an API key now removes the provider**: wiping the API key text box and saving previously had no visible effect — the empty config was persisted and the provider card remained. The settings window now calls `RemoveConfigAsync` for standard API key providers with an empty key and immediately removes the card from the panel.

## [2.3.4-beta.19] - 2026-04-01

### Added
- **HTTP failure classification model**: structured `HttpFailureClassification` enum and `HttpFailureContext` record provide a shared vocabulary for HTTP/network failure types across all providers (`Authentication`, `Authorization`, `RateLimit`, `Network`, `Timeout`, `Server`, `Client`, `Deserialization`).
- **Centralized HTTP failure mapper**: `HttpFailureMapper` is the single source of truth for classifying HTTP responses and exceptions — used by `HttpRequestBuilderExtensions` and providers.
- **Classification-aware circuit-breaker backoff**: rate-limited providers now use the server-supplied `Retry-After` delay instead of blind exponential backoff. Rate limits without `Retry-After` use flat base backoff (1 min) rather than escalating exponential growth.
- **Operator diagnostics for open circuits**: `ProviderRefreshDiagnostic.LastFailureClassification` and `RefreshTelemetrySnapshot.OpenCircuitsByClassification` expose why circuits are open (auth failure, rate limit, server error, network issue).
- **Provider contract formalized**: `IProviderService.GetUsageAsync` documents the no-throw contract and optional `FailureContext` attachment convention; `DeepSeekProvider` and `GeminiProvider` are pilot adopters.

## [2.3.4-beta.18] - 2026-03-30

### Changed
- **Test release**: verifies beta.17's in-app update fix (FileStream lock + no UAC) works end-to-end.

## [2.3.4-beta.17] - 2026-03-30

### Fixed
- **Installer download failed with file lock on move**: `DownloadInstallerAsync` used `using var` (declaration-scoped) for the `FileStream`, keeping the file handle open until end-of-method. `File.Move` then failed with an `IOException` because the `.partial` file was still locked. Switched to block-scoped `using` so the stream is disposed before the move.

### Added
- **Download-then-move regression test**: end-to-end test downloads a real appcast XML to a `.partial` temp file using the same `FileStream` block pattern, then moves it to the final path. Catches the exact bug: if someone reverts to `using var`, the file lock causes the move to fail.

## [2.3.4-beta.16] - 2026-03-30

### Changed
- **Test release**: no code changes — verifies that beta.15's in-app update (without UAC elevation) can download and install beta.16 successfully.

## [2.3.4-beta.15] - 2026-03-30

### Fixed
- **Update download failed silently — installer used unnecessary UAC elevation**: `Verb = "runas"` forced a UAC prompt even though the Inno Setup installer uses `PrivilegesRequired=lowest`. The UAC prompt was hidden behind the topmost main window, causing the install to fail silently. Removed the `runas` verb.
- **Changelog and download progress windows hidden behind main window**: both now inherit `Topmost` from the main window.
- **Update failure reason was invisible**: `DownloadAndInstallUpdateAsync` now returns `UpdateInstallResult` with a specific failure reason (HTTP status, timeout, file I/O, UAC rejection) shown in both the error dialog and the diagnostic log.

## [2.3.4-beta.14] - 2026-03-30

### Fixed
- **Codex and Spark are now independent providers**: removed the parent-child hierarchy that caused endless layout bugs. "OpenAI (Codex)" and "OpenAI (GPT-5.3 Codex Spark)" are now two standalone providers, each with their own burst+rolling dual bars.
- **Spark card shows both 5-hour and weekly bars**: Spark previously collapsed its burst and rolling windows into a single card. Now emits separate burst+rolling usages so the dual-bar toggle works consistently.
- **Settings not persisted when closing window**: checkbox events during initialization overwrote preferences with defaults; closing via X button killed the auto-save timer before it fired.

### Added
- **Card catalog screenshot generator**: 16 screenshots showing every card setting permutation (presets, compact mode, background bar, dual bars, pace adjustment, show used, reset time, badge slots) with auto-generated markdown documentation. Run via `--card-catalog` or `scripts/generate_card_catalog.ps1`.

## [2.3.4-beta.13] - 2026-03-30

### Fixed
- **Update errors were invisible**: `ILogger` output from the update checker had no file sink in the UI app — download/install failures vanished silently. Added `[UPDATE]` diagnostic log entries at every decision point and the download URL is now shown in the error dialog.

### Added
- **End-to-end update pipeline tests**: 19 integration tests against the live GitHub Releases API and CDN verify the full update flow — beta update check, download URLs for all architectures return HTTP 200, release assets exist with non-zero sizes, appcast files are valid XML with correct versions and lengths, stable appcast URLs resolve, and the GitHub API contract hasn't changed.

## [2.3.4-beta.12] - 2026-03-30

### Fixed
- **OpenAI Codex showed dual-bar layout instead of 3 flat cards**: `BuildModels()` filtered flat model cards by `WindowKind == None`, but Codex uses `Burst`/`Rolling`/`ModelSpecific` — all cards fell through to `LegacyParentCardBuilder` which assembled a dual-bar. `FamilyMode.FlatWindowCards` is now checked first as an early return, projecting all cards as flat models regardless of `WindowKind`.
- **Beta appcast files committed with missing enclosure length**: the `appcast_beta*.xml` files had placeholder content instead of real release items with installer sizes. All 4 beta appcast files now contain the actual 2.3.4-beta.11 release data with correct byte counts.
- **Settings window silently reverted main window preference changes**: toggling "Show Used" on the main window and then changing "Pace-aware colours" in the already-open settings window overwrote "Show Used". The settings window was loading a separate `AppPreferences` from disk instead of using the shared in-memory instance.

### Added
- **End-to-end appcast regression tests**: `AppcastXmlConsistencyTests` validates all 8 committed appcast files on every test run — non-zero installer length for beta feeds, URL/version/architecture consistency, default feed identical to x64, and Sparkle attribute correctness. `UpdateChannelConfigurationEndToEndTests` verifies `generate-appcast.sh` reads `INSTALLER_SIZE_*` env vars correctly. `verify-release-artifacts.ps1` now checks `length > 0` in the publish pipeline.

## [2.3.4-beta.11] - 2026-03-30

### Fixed
- **Claude Code cards now show "Claude Code (Current Session)" etc.**: flat cards were displaying bare names ("Current Session", "Sonnet", "All Models") with no provider context. `FlatCardShowProviderPrefix` on `ProviderDefinition` enables prefixing for providers whose card names are generic.
- **Claude Code "Current Session" and "All Models" cards not appearing**: stale DB rows stored `WindowKind.Burst` / `WindowKind.Rolling` from older Monitor binaries. `GetLatestHistoryAsync` now re-derives `WindowKind` from the canonical `QuotaWindowDefinition` using `ChildProviderId` matching, so stale values are corrected on load without a DB migration.
- **OpenAI Codex "Spark" appeared as a standalone flat card**: `Spark` had no `WindowKind` in older DB rows (NULL → `None`), so it incorrectly passed the flat-model filter. The same `WindowKind` re-derivation now correctly marks it `ModelSpecific`, routing it to the combined "OpenAI (Codex)" card.
- **Flat-card projection gated on `FamilyMode`**: `GroupedUsageProjectionService.BuildModels` now uses a pure data-driven rule (`WindowKind == None`) instead of consulting `FamilyMode`, removing the `isFlatWindowCards`/`hasModelCards` guards.

## [2.3.4-beta.10] - 2026-03-29

### Fixed
- **Settings window showed provider as "Inactive" when running**: flat provider cards were being created with a namespaced `ProviderId` (e.g. `antigravity.gemini-pro`) instead of the provider's own `ProviderId`. The settings lookup now correctly finds the card because `ProviderId` is the provider identifier and `CardId` is the model identifier.
- **"Other providers (N)" sub-headers removed**: the active/inactive sub-grouping inside each section was removed. Only the two top-level sections ("Plans & Quotas" and "Pay As You Go") remain.

### Added
- **"Show model information when not running" checkbox**: AntiGravity (and other auto-detected flat-card providers) now have a "Models offline" checkbox in Settings. When enabled, the last-fetched per-model cards are shown with a stale indicator even when the process is not running.

## [2.3.4-beta.9] - 2026-03-28

### Changed
- **Flat provider card model**: replaced the `ProviderUsage` + `ProviderUsageDetail` parent-child structure with a flat list of independent cards. Each card has a stable `CardId` (database key), `GroupId` (rendering group), `WindowKind`, and `ModelName`. Grouping is now a rendering concern, not a data structure concern.
- **Claude Code**: All Models, Sonnet, Opus, and Current Session are now explicit named cards rather than sub-details of a single parent card.
- **Database migrations V12 and V13**: add `card_id`, `group_id`, `window_kind`, `model_name`, and `is_stale` columns to the history table.
- Removed `ProviderUsageDetail` and `ProviderUsageDetailType` entirely (−2 900 net lines).

### Fixed
- Beta update checker now correctly detects new versions.

## [2.3.4-beta.8] - 2026-03-27

### Changed
- Dependency updates: CommunityToolkit.Mvvm, SignalR.Client, Microsoft.Extensions.\*, System.\*, coverlet, Meziantou.Analyzer, Playwright, MSTest, System.Reactive, dotnet-sdk 8.0.419, codecov-action v6, codeql-action, setup-dotnet

### Fixed
- Resolve remaining CodeQL code scanning alerts: useless upcasts in JSON helpers, `Path.Combine` → `Path.Join` in MonitorLauncher, narrowed generic catch clauses

## [2.3.4-beta.7] - 2026-03-27

### Fixed
- **Quota bar color changes when toggling dual bars on/off**: bar rows now use the same pace-adjusted color as single-bar mode. Previously the dual bar rows used the raw used percentage for color while single-bar mode used the pace-projected value, causing a visible color shift when toggling.
- **Stale cache entries driving rendering**: `ProviderDetails` now only contains fresh, provider-level `QuotaWindow` entries. Stale DB entries with a wrong `detail_type` (from older Monitor binaries) and model-scoped quota windows are excluded — rendering is now driven entirely by what the provider class emits, not by cached data.

### Added
- **Windows startup settings in Settings → Layout**: two checkboxes — "Start Monitor with Windows" and "Start UI with Windows" — write directly to `HKCU\...\Run`, so they take effect immediately without reinstalling.
- **Installer startup options now use the registry**: the optional "Run at Windows Startup" installer tasks now write to the same registry Run key as the settings dialog, ensuring both mechanisms are in sync and the settings checkboxes correctly reflect what the installer set up.

## [2.3.4-beta.6] - 2026-03-27

### Fixed
- **Pace calculation wrong for Claude Code**: `NextResetTime` now uses the 7-day rolling reset (matching `PeriodDuration = 7d`). Previously it used the 5-hour burst reset, anchoring a 7-day period calculation to the wrong window — causing ~97% of the period to appear elapsed and distorting the pace projection.
- **Sonnet and weekly usage not visible on Claude Code card**: `ProviderUsage.Details` now flows directly as `ProviderDetails` without being split/filtered. Sonnet, Opus, and All Models details all appear on the parent card as expected.
- **Claude Code rendered as single parent card with dual quota bars**: the 5-hour burst and 7-day rolling windows are shown as dual bars on one card. Per-model child rows (which showed incorrect usage values) have been removed.
- **Dual bar label scraping**: bar labels now use `DualBarLabel` directly from the catalog definition instead of scraping them from the status text string.
- **CodeQL code scanning alerts**: resolved 92 CodeQL alerts across the codebase.

### Changed
- **ProviderDetails as single source of truth**: replaced the `ProviderQuotaDetails`/`Models` split with direct `ProviderUsage.Details` passthrough. The UI reads `QuotaWindow` details to drive dual bars and `Model` details to drive detail rows — no separate extraction step.
- **Unified JSON serialization**: `MonitorJsonSerializer` is the single entry point for all Monitor API serialization/deserialization.
- **Dead display modes removed**: `CollapsedDerivedProviders` and `SyntheticAggregateChildren` family modes deleted (~200 lines).

## [2.3.4-beta.5] - 2026-03-26

### Fixed
- **Pace badge / bar color disagreement**: bar color no longer turns red when the pace badge says "On pace". Both now derive from a single projection — structurally impossible to disagree.
- **Card designer preview not updating**: changing Display Options (usage rate, dual bars, pace colours, thresholds) now immediately updates the live preview.
- **Settings changes not applied to main window**: closing the Settings dialog (via Close button, X, or Alt+F4) now always reloads preferences. Root cause: `DialogResult` was never set, so the main window skipped the reload.
- **Tray icon colours ignore pace adjustment**: tray icons now use the same pace-adjusted colour as the main window, respecting the user's Enable Pace-Aware Colours setting.
- **Web dashboard uses different colour thresholds**: web UI now reads the shared `preferences.json` instead of hardcoded 50/90 values.
- **Dual/single bar toggle not taking effect**: toggling "Show dual quota bars" in Settings now correctly switches between dual and single bar rendering after dialog close.

### Added
- **Configurable card slots**: card designer slot choices (Primary Badge, Secondary Badge, Status Line, Reset Info) are now persisted in preferences and used by the real card renderer — preview matches main window exactly.
- **Slot rendering tests**: 8 regression tests verify that boolean toggles (ShowUsagePerHour, EnablePaceAdjustment, UseRelativeResetTime) are respected by slot-based rendering.

### Changed
- **Unified pace/colour service**: `ComputePaceColor()` replaces 5 separate methods (`CalculatePaceAdjustedColorPercent`, `GetColorIndicatorPercent`, `GetPaceBadge`, `GetPaceBadgeText`, `CalculateProjectedFinalPercent`). Single computation, consistent results.
- **Shared preferences service**: `IPreferencesStore` + `PreferencesStore` in Core/Infrastructure replaces 3 independent file readers (Desktop `UiPreferencesStore`, Monitor `JsonConfigLoader`, Web inline `File.ReadAllText`).
- **Card designer uses real renderer**: preview now calls `ProviderCardRenderer.CreateProviderCard()` — same rendering path as the main window. ~300 lines of duplicate card-building code deleted.
- **Dead MVVM layer removed** (~1,700 lines): `ProviderCardViewModel`, `SubProviderCardViewModel`, `CollapsibleSectionViewModel`, `ProviderCardResources.xaml`, `CollapsibleSectionResources.xaml`, and `MainViewModel.UpdateSections` were instantiated but never connected to the UI.
- **Settings apply without file round-trip**: main window reads `App.Preferences` in-memory instead of reloading from disk after settings close.

## [2.3.4-beta.4] - 2026-03-25

### Added
- **Pace badge delta text**: pace badges now show projected delta (e.g. "+12%", "-30%") alongside the tier label.

### Dependencies
- Bump Meziantou.Analyzer from 3.0.25 to 3.0.27.
- Bump github/codeql-action from 4.34.0 to 4.34.1.

## [2.3.4-beta.3] - 2026-03-24

### Added
- **Check for Updates button**: Settings > Updates tab now has a manual "Check for Updates" button with inline status feedback.
- **Dual quota bar toggle**: users can now disable dual bars and select whether the single bar reflects the rolling (weekly) or burst (hourly) window.

### Fixed
- **Reset timer showing 0m**: countdown timer was off by the local UTC offset due to SQLite/Dapper returning `DateTime` with `Kind=Unspecified`. Added Dapper `UtcDateTimeHandler` to tag all database reads as UTC.
- **UTC enforcement**: all internal timestamps now use UTC. `DateTime.Now` replaced with `DateTime.UtcNow` in providers (Gemini, Antigravity, Kimi, OpenRouter) and `ResetTimeParser`. Local time conversion only at display boundary.
- **Single-bar status and reset badges**: when dual bars are disabled, the status text and reset badge now reflect the selected quota window.
- **Tray tooltip consistency**: tray icon status text now matches the selected single-window mode when dual bars are off.

### Changed
- **Provider icon backdrop**: dark-theme provider icons now render on a rounded square backdrop instead of a white outline, with a slightly brighter fill for legibility.

## [2.3.4-beta.2] - 2026-03-23

### Fixed
- **Graceful shutdown via CancellationToken propagation**: in-flight provider HTTP requests are now properly cancelled during shutdown instead of being abandoned. Token threaded from job scheduler through the entire refresh pipeline to individual providers.
- **Thread-safe ProviderManager replacement**: `Volatile.Read` / `Interlocked.Exchange` prevent race conditions when ProviderManager is swapped during concurrency adjustments.
- **Hardcoded test credentials removed**: all `ApiKey` string literals across 22 test files replaced with `Guid.NewGuid()` to eliminate security scanner warnings.

### Performance
- **Grouped usage projection cache**: `/api/usage/grouped` endpoint caches the O(n²) projection for 30 seconds, eliminating redundant computation on repeated calls.
- **Config and preferences in-memory cache**: disk reads eliminated for repeat calls; cache invalidates automatically on save.
- **SignalR delta detection**: "UsageUpdated" broadcast suppressed when provider data hasn't meaningfully changed, reducing unnecessary UI updates.

### Changed
- **DI factory elimination**: `ProviderRefreshServiceFactory` removed; all sub-services registered as proper DI singletons, improving testability and lifetime management.
- **CI diagnostics**: OpenAPI contract check now captures Monitor stdout/stderr on startup failure.

## [2.3.4-beta.1] - 2026-03-23

### Fixed
- **Stale badge missing on parent providers**: providers like Antigravity that fetch successfully but have all-stale child data now show the stale badge.

## [2.3.3] - 2026-03-22

### Added
- **Pace projection badges**: 3-tier system — Headroom / On pace / Over pace — with projected end-of-period usage percentage.
- **Burst/weekly labels on dual bars**: providers with two quota windows show labels (e.g. "5h" / "Weekly") from metadata.
- **Stale data badge**: red "Stale" badge + dimmed card when provider data is outdated.
- **Card Designer** in Settings → Cards: customizable card slots, presets, compact mode.
- **Auto-collapse inactive providers** into expandable section.
- **Configurable reset time format**: absolute or relative, per user preference.

### Performance
- **Startup ~19s → ~3.5s**: monitor launches in parallel with UI initialization.

### Fixed
- **Pace calculation reworked**: simple linear projection replaces broken cubic formula. Single source of truth — no more duplicated pace math.
- **Codex reset badge**: no longer shows Spark's reset time on the parent card.
- **Stale detection broken**: was scanning detail-level flags instead of reading provider-level `IsStale`.
- Multiple startup bugs fixed (DI resolution, ConfigureAwait deadlock, HTTP timeout).
- Replaced fragile `ReferenceEquals` on catalog objects with value-based comparison.
- Eliminated redundant catalog lookups and unused fallback chains.

### Changed
- **~7,000 lines removed**: dead code, Polly stack, duplicate interfaces.
- **Settings UI redesigned**: new Cards/Layout tabs, two-column layouts.
- Dark SVG icons visible on dark themes.

## [2.3.2-beta.7] - 2026-03-20

### Fixed
- Pace-adjusted quota colours now apply correctly to model-specific detail cards (for example Claude Sonnet/Opus) by propagating declared period durations through the catalog pipeline.
- Legacy database compatibility bootstrap now ensures `provider_history.next_reset_time` exists before timestamp conversion, preventing migration failures on older schemas.
- Web usage/history mapping now handles both epoch and text timestamps consistently, restoring correct reliability and latest-row projections.

### Changed
- Removed fallback heuristics in quota/reset presentation paths; dual-bucket and reset badge rendering now resolve from provider metadata declarations.
- Simplified percentage parsing to explicit supported formats while preserving plain numeric values (for example `"50"`).
- Normalized formatting/newline/encoding and analyzer baseline configuration so release pre-commit validation passes cleanly.

### CI/CD
- **NuGet caching added to `publish` workflow**: All 6 matrix jobs (win-x64/x86/arm64, linux-x64, osx-x64/arm64) now share a NuGet package cache, eliminating redundant restores on every publish run.
- **Playwright browser caching**: Chromium (~300MB) is now cached in `web-tests-windows` keyed on the Web.Tests project file — skipped on cache hit.
- **Cross-platform test job now uses NuGet cache**: The Ubuntu core-test job was the only job without NuGet caching.
- **CodeQL runs on push to main/develop**: Previously weekly only; now also triggered on source code changes post-merge.
- **OSSF Scorecard**: Added supply-chain security scoring, publishes results to securityscorecards.dev and GitHub Code Scanning.
- **Gitleaks secret scanning**: Added secret scanning on every PR and push; scans only PR-introduced commits on pull requests, full history on push.
- **GitHub Actions updated**: trivy-action v0.29.0→v0.35.0, scorecard-action v2.4.0→v2.4.3, codeql-action/upload-sarif v3→v4.34.0 (consistent across all workflows), actions/cache v5.0.3→v5.0.4.
- **Resolved zizmor template-injection alerts**: `publish.yml` and `pr-size-check.yml` `${{ }}` expressions moved from inline shell/JS into `env:` vars.

## [2.3.2-beta.6] - 2026-03-20

### Fixed
- **Pace-adjusted colour not applied to Sonnet card**: `ResolveRollingWindowInfo()` path 2 searched only for `WindowKind.Rolling` details — which excluded the Sonnet card (`WindowKind.ModelSpecific`) — so `ColorIndicatorPercent` fell back to the raw used percentage and rendered yellow despite the user being well under pace.

### Refactored
- **Removed `ResolveRollingWindowInfo()` fallback chain**: `ProviderUsageDisplayCatalog.ExpandSyntheticAggregateChildren` now calls `EnrichWithPeriodDuration` on every non-aggregate usage before yielding it, setting `PeriodDuration` directly from the catalog's primary rolling window definition. `ProviderCardViewModel.ColorIndicatorPercent` and `PaceBadgeText` now read `Usage.PeriodDuration` and `Usage.NextResetTime` directly — one condition, no catalog lookup, no detail iteration.
- **`CreateAggregateDetailUsage` uses `detail.NextResetTime ?? parentUsage.NextResetTime`**: ensures the child card always has a reset time for pace calculation even when the provider detail's own reset time is absent.

## [2.3.2-beta.5] - 2026-03-20

### Fixed
- **Pace-adjusted bar still yellow when clearly under pace**: The quadratic forgiveness formula (`usedPercent² / expectedPercent`) produced a pace-adjusted value of ~60.2% for Sonnet at 73% used with 88.5% of the 7-day window elapsed — barely above the 60% yellow threshold despite being well under pace. Changed to a cubic formula (`usedPercent³ / expectedPercent²`) which gives 49.7% in the same scenario, correctly rendering the bar green. The new formula is more forgiving for providers that are meaningfully under pace while still warning when genuinely approaching the limit.

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
