# Changelog

## [Unreleased]

### Fixed
- **Stale Codex / session-auth data after DB wipe**: Unavailable provider entries that carry a description (e.g. "Codex auth token not found", "API Key missing") are no longer silently dropped by the processing pipeline. They are now stored in the database and displayed in the UI, so customers see actionable re-auth messages instead of stale cached data that disappears after a database wipe.

## [2.3.1-beta.1] - 2026-03-18

### Removed
- **AnthropicProvider**: Removed the non-functional stub provider that returned hardcoded responses with no real API integration.

### Changed
- **ProviderBase Helpers**: Added `CreateBearerRequest()` and `DeserializeJsonOrDefault<T>()` helpers to `ProviderBase`, eliminating repeated boilerplate across all provider implementations.

### CI/CD
- Updated all GitHub Actions to latest major versions (checkout v6, setup-dotnet v5, upload-artifact v7, download-artifact v8, github-script v8, cache v5, codecov v5, create-pull-request v8, paths-filter v4) to eliminate Node.js 20 deprecation warnings.

## [2.3.0] - 2026-03-16

### Added
- **Card Visibility Controls**: Settings → Providers tab now has a "Card Visibility" section listing every card currently shown in the main window. Each card has an individual checkbox to hide or show it. Standalone providers (Kimi, Z.ai, etc.) appear as flat checkboxes; multi-card providers (Codex, Gemini, Antigravity, Claude Code) appear under a bold heading with one checkbox per sub-card. The list is derived from the same live pipeline as the main window, so it automatically includes all runtime model cards (e.g. every Antigravity model, every Gemini quota window) without any static configuration.
- **Claude Code OAuth Integration**: OAuth usage endpoint for Claude subscription users with dual quota buckets (5-hour burst + 7-day rolling window), per-model breakdowns (Sonnet/Opus), and extra usage status.
- **Dual Progress Bars**: Full support for displaying burst and rolling quota bars simultaneously on Kimi, OpenAI Codex, and GitHub Copilot cards.
- **Burn Rate Forecasting**: Forecast methods, confidence levels, and trend detection for provider usage projections.
- **Gemini CLI Auth Fallback**: Provider-level fallback from OpenCode Antigravity accounts to native Gemini CLI auth files (`.gemini/oauth_creds.json`), with account identity extraction and deterministic project resolution.
- **DB Schema**: Added `parent_provider_id` column to `provider_history` and a foreign-key constraint on `raw_snapshots` for richer lineage queries.
- **WPF Architecture**: Declarative attached behaviors (`WindowDragBehavior`, `KeyboardShortcutBehavior`, `CloseWindowBehavior`), design-time ViewModels, Rx-based polling service, and `WeakEventManager` for memory-safe event subscriptions.

### Fixed
- **Z.ai Reset Timer**: When the 5-hour rolling window is fresh (0% used), the API returns the billing period end date (~8 days away) as `nextResetTime`. The UI was showing "7 days 17 hours" for a limit that resets every 5 hours. Now parses `unit`/`number` fields to show a "5h window" label instead. When the window is active the actual close time is shown as before.
- **Codex Spark Weekly Quota**: Spark's child card was showing the main Codex weekly usage instead of its own independent counter. Fixed by preferring Spark's own secondary window and falling back to the main weekly only when absent.
- **Kimi Dual Bar Wrong Percentage**: Kimi card was inverting the weekly bar due to a fallback in `ProviderDualQuotaBucketPresentationCatalog` that silently reused the same `WindowKind` for both buckets. Removed the fallback — dual bar now requires two genuinely different `WindowKind`s.
- **Codex Dual Bars**: Both quota bars (5-hour burst + weekly rolling) were not rendering because `NormalizeDetails` dropped `PercentageValue`/`PercentageSemantic` when copying detail objects. Fixed field preservation in the processing pipeline.
- **Claude Code OAuth Token Staleness**: Monitor was reusing an expired OAuth access token (valid ~1 hour). Provider now re-reads `~/.claude/.credentials.json` on every request.
- **Claude Code Timeout**: Polly retry-on-429 policy was consuming ~14s of the 25s budget. Providers now use a plain `HttpClient` without retry policies and manage their own fallback chains.
- **Kimi Provider ID**: Renamed from `kimi` to `kimi-for-coding` to match OpenCode's `auth.json` key format. Auth file discovery extended to `~/.opencode/auth.json`.
- **Monitor Auto-Restart on Version Mismatch**: Slim UI now detects a stale monitor process after an upgrade and restarts it automatically.
- **Quota Percentage Calculation**: OpenAI and Codex providers now use the highest usage across all quota windows (primary, secondary, spark) for the parent card, fixing incorrect 0% when only secondary/spark had usage.
- **Auth Discovery**: Restored legacy/shared OpenCode auth file locations; fixed Synthetic and Z.AI key resolution from `~/.local/share/opencode/auth.json`; reordered priority so fresh keys win.
- **Provider DI Registration**: Fixed `HttpClient` singleton registration in the Monitor DI container, resolving startup errors for `ClaudeCodeProvider` and `KimiProvider`.
- **OpenAI Dual Bars**: Legacy database records used PascalCase `"WindowKind"` key, causing deserialization to silently drop the value. Normalized on read.

### Changed
- **Breaking — Model Cleanup**: Removed `RequestsPercentage`, `UsageUnit`, and `Used` string properties. Removed legacy `WindowKind` aliases (`Primary`, `Secondary`, `Spark`). Consumers should use `UsedPercent`/`RemainingPercent` and `WindowKind.Burst`/`Rolling`/`ModelSpecific`.
- **Providers as Source of Truth**: Provider definitions are the single authority for discovery, canonicalization, visibility, visuals, session-auth ownership, and quota window declarations. Removed all string-inspection heuristics from the pipeline.
- **Architecture Simplification**: Removed `ProviderCapabilityCatalog` (96-line pass-through layer). Collapsed `ExpandSyntheticAggregateChildren` logic into `ProviderUsageDisplayCatalog`. Dual-bucket percentages computed once in `ProviderCardPresentationCatalog` and carried on the presentation record.
- **No Console Window**: Monitor and Web server no longer open a console window on Windows (`OutputType=WinExe`).
- **Auth Source Precedence**: App-local auth (`%LOCALAPPDATA%\AIUsageTracker\auth.json`) is now the final auth source, overriding earlier sources. Config paths deduplicated when multiple entries resolve to the same file.

## [2.2.26] - 2026-02-28

## [2.2.26] - 2026-02-28

### Added
- Dual release channel support (Stable and Beta)
- Update channel selector in Settings window
- develop branch for beta releases

### Changes
- Solution file updated to reference AIUsageTracker.* projects
- App icon now properly embedded in all executables

### CI/CD
- New release.yml workflow with channel parameter
- publish.yml updated to detect beta releases from tag patterns
- generate-appcast.sh script with channel support
- Beta appcast XML files for all architectures

### Application
- UpdateChannel enum in Core (Stable/Beta)
- GitHubUpdateChecker now uses channel-specific appcast URLs

## [2.2.25] - 2026-02-27

### Added
- Pay-as-you-go support for Mistral AI
- Automatic retry for Gemini API on common failures

### Fixed
- Anthropic authentication error handling
- Layout issues on high-DPI displays
