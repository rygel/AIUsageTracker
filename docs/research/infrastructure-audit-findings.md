# Infrastructure Provider Code Reduction Audit

Date: 2026-06-11
Cycle: 16
Status: Draft — pending owner approval

## Summary

| Metric | Value |
|--------|-------|
| Total providers | 16 |
| Total lines (provider files only) | ~7,115 |
| Providers using `FetchJsonAsync<T>` | 4/16 |
| Estimated direct line savings | ~275-345 |
| Estimated lines moved to helpers | ~537 |

## Provider Inventory

| # | Provider | Lines | Uses FetchJsonAsync | Response DTOs | Auth Complexity |
|---|----------|-------|---------------------|---------------|-----------------|
| 1 | AntigravityProvider | 1102 | No | 11 | Process discovery + CSRF |
| 2 | ClaudeCodeProvider | 698 | No | 4 | OAuth + API key + CLI |
| 3 | CodexProvider | 665 | No | 2 | JWT + native auth files |
| 4 | GeminiProvider | 748 | No | 7 | OAuth refresh + file discovery |
| 5 | GitHubCopilotProvider | 559 | No | 1 | OAuth + device flow |
| 6 | KimiProvider | 370 | Yes | 5 | Bearer token |
| 7 | MinimaxProvider | 459 | No | 3 | Bearer + multi-endpoint |
| 8 | OpenAIProvider | 433 | No | 0 | Bearer + session + JWT |
| 9 | OpenCodeProvider | 188 | No | 2 | Bearer + env var |
| 10 | OpenCodeZenProvider | 742 | No | 2 | CLI path resolution |
| 11 | OpenRouterProvider | 346 | No | 4 | Bearer token |
| 12 | SyntheticProvider | 394 | No | 0 | Bearer token |
| 13 | ZaiProvider | 443 | No | 3 | Raw key (non-Bearer) |
| 14 | DeepSeekProvider | 160 | Yes | 3 | Bearer token |
| 15 | XiaomiProvider | 123 | Yes | 2 | Bearer token |
| 16 | MistralProvider | 98 | No | 0 | Bearer + env var |

## Category 1: Easy Wins (~1 hour total)

### 1A. CreateBaseUsage() Helper on ProviderBase

Every provider repeats this 4-line block ~40 times across all files:

```csharp
ProviderId = this.ProviderId,
ProviderName = providerLabel,
IsQuotaBased = this.Definition.IsQuotaBased,
PlanType = this.Definition.PlanType,
```

**Recommendation**: Add `ProviderBase.CreateBaseUsage(string providerLabel)` that pre-populates these fields.

**Impact**: ~80-120 lines saved across all 16 providers.
**Effort**: 1 hour.
**Risk**: Low — mechanical refactoring, all tests pass if field values match.

### 1B. OpenCodeProvider can use FetchJsonAsync

**File**: `OpenCodeProvider.cs`, lines 83-168
**Issue**: Manual Bearer request + status check + deserialize + error catch. Nearly identical to `FetchJsonAsync<T>`.
**Caveat**: Has a non-JSON content-type check (line 102-109) that returns empty array.
**Recommendation**: Adopt `FetchJsonAsync<T>` with a content-type guard wrapper. ~40 lines saved.

### 1C. OpenRouterProvider credits fetch can use FetchJsonAsync

**File**: `OpenRouterProvider.cs`, lines 69-128
**Issue**: Credits fetch is a textbook `FetchJsonAsync<T>` candidate. Key info fetch has different error handling (warns but continues).
**Recommendation**: Convert credits portion only. ~30 lines saved.

## Category 2: Medium Effort (~half day each)

### 2A. Shared Rate Limit Parser (OpenAI + Codex)

**Files**:
- `OpenAIProvider.cs`, lines 132-213: `ParseOpenAiSessionWindows`, `ResolveResetTime`, `ResolveWindowResetTime`
- `CodexProvider.cs`, lines 280-354: `ParseAdditionalRateLimits`, `ParseRateLimitProperties`, `TryParseSparkWindowFromElement`

Both parse identical `rate_limit.primary_window` / `secondary_window` JSON structures with identical constants:
```csharp
private const string JsonKeyRateLimit = "rate_limit";
private const string JsonKeyPrimaryWindow = "primary_window";
private const string JsonKeySecondaryWindow = "secondary_window";
```

**Recommendation**: Extract a shared `WhamRateLimitParser` class. Both providers call into it.
**Impact**: ~80-100 lines saved.
**Risk**: Medium — Codex has additional `additional_rate_limits` and Spark-specific logic. Shared parser handles base windows only.

### 2B. MinimaxProvider Duplicate URL Resolution

**File**: `MinimaxProvider.cs`, lines 98-136
**Issue**: `GetTokenUsageAsync` and `GetCodingPlanUsageAsync` are nearly identical — only differ by default endpoint URL.
**Recommendation**: Merge into single method with a URL parameter. ~15 lines saved.

### 2C. JsonFileLoader Helper (Gemini + Codex)

**File**: `GeminiProvider.cs`, ~5 file-reading methods all following:
```csharp
if (!File.Exists(path)) return null;
try { var json = File.ReadAllText(path); return JsonSerializer.Deserialize<T>(...); }
catch (Exception ex) when (...) { _logger.LogError(...); return null; }
```

**Recommendation**: Extract `JsonFileLoader.TryLoadFile<T>(path, logger)`.
**Impact**: ~30-40 lines saved in GeminiProvider, reusable for CodexProvider auth file reading.

## Category 3: Structural (requires design decisions)

### 3A. AntigravityProvider Model Grouping Extraction

**File**: `AntigravityProvider.cs`, lines 310-577 (~267 lines)
**Issue**: Model grouping/sorting logic is pure data transformation unrelated to HTTP or process discovery.
**Recommendation**: Extract `AntigravityModelGrouper` class. No line reduction but significantly better maintainability.
**Impact**: ~267 lines moved.

### 3B. OpenCodeZenProvider CLI Path Resolution Extraction

**File**: `OpenCodeZenProvider.cs`, lines 448-717 (~270 lines)
**Issue**: CLI path resolution (`ResolveCliPathAsync`, `IsInPathAsync`, etc.) is general "find a CLI tool" utility.
**Recommendation**: Extract to `CliToolResolver` utility class. Reusable for future CLI-based providers.
**Impact**: ~270 lines moved.

### 3C. ZaiProvider Non-Bearer Auth

**File**: `ZaiProvider.cs`, lines 63-67
**Issue**: Uses raw API key without "Bearer" prefix, so `FetchJsonAsync<T>` won't work.
**Recommendation**: If `FetchJsonAsync<T>` accepted an auth customization delegate, Z.ai could use it. Structural change to base class API.

## Providers Already Lean (No Action Needed)

- **MistralProvider** (98 lines) — Already minimal
- **XiaomiProvider** (123 lines) — Uses `FetchJsonAsync<T>`, clean
- **DeepSeekProvider** (160 lines) — Uses `FetchJsonAsync<T>`, clean
- **KimiProvider** (370 lines) — Uses `FetchJsonAsync<T>`, reasonable size

## Recommended Priority Order

1. `CreateBaseUsage()` helper (1A) — Highest ROI, all providers, safest
2. MinimaxProvider URL dedup (2B) — Quick, obvious
3. OpenCodeProvider → FetchJsonAsync (1B) — Straightforward
4. OpenRouterProvider credits → FetchJsonAsync (1C) — Same pattern
5. JsonFileLoader helper (2C) — Gemini main beneficiary
6. Shared WhamRateLimitParser (2A) — Most impactful medium effort
7. CliToolResolver extraction (3B) — Large, do when stable
8. AntigravityModelGrouper extraction (3A) — Same as above

## Quantified Savings

| Finding | Lines Saved | Effort |
|---------|-------------|--------|
| CreateBaseUsage() helper | 80-120 | 1 hour |
| OpenCodeProvider → FetchJsonAsync | ~40 | 30 min |
| OpenRouterProvider → FetchJsonAsync | ~30 | 30 min |
| Shared WhamRateLimitParser | 80-100 | 4 hours |
| MinimaxProvider URL dedup | ~15 | 30 min |
| JsonFileLoader helper | 30-40 | 2 hours |
| CliToolResolver extraction | 270 (moved) | 3 hours |
| AntigravityModelGrouper extraction | 267 (moved) | 3 hours |
| **Total direct savings** | **~275-345** | |
| **Total extracted/moved** | **~537** | |
