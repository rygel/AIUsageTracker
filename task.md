# Architecture Improvement Tasks

Generated: 2026-03-04

## Goal
Improve architecture and remove duplicated code while preserving current behavior (especially provider visibility and startup non-hammering behavior).

## Priority Backlog

### P1: Single Source of Truth for Providers

1. Replace manual provider wiring in monitor with centralized DI-based registration.
   - Current duplication:
     - `AIUsageTracker.Monitor/Services/ProviderRefreshService.cs:141` to `:177` manually instantiates provider list.
     - `AIUsageTracker.Infrastructure/Providers/ProviderMetadataCatalog.cs:7` to `:27` hardcodes definitions separately.
   - Risk today:
     - Provider list and metadata list can drift.
     - Adding/removing provider requires edits in multiple files.
   - Refactor target:
     - One provider registry service used by Monitor/Web/UI.
     - Register providers via DI and derive metadata from registered implementations.

2. Adopt or remove dead registration abstraction.
   - Candidate currently unused:
     - `AIUsageTracker.Infrastructure/Extensions/ProviderRegistrationExtensions.cs:13`
   - Action:
     - Either wire this extension into startup or delete it if not used.

### P1: Shared Provider Result/Error Pipeline

3. Normalize provider error/result creation.
   - Current duplication:
     - Inline `new ProviderUsage` error payloads repeated in many providers, e.g.:
       - `AIUsageTracker.Infrastructure/Providers/OpenRouterProvider.cs:35`
       - `AIUsageTracker.Infrastructure/Providers/OpenRouterProvider.cs:68`
       - `AIUsageTracker.Infrastructure/Providers/KimiProvider.cs`
       - `AIUsageTracker.Infrastructure/Providers/DeepSeekProvider.cs`
       - `AIUsageTracker.Infrastructure/Providers/MistralProvider.cs`
   - Existing base support:
     - `AIUsageTracker.Core/Providers/ProviderBase.cs` has `CreateUnavailableUsage*` helpers.
   - Refactor target:
     - Use base helper methods consistently.
     - Add shared helper for common success payload fields.

4. Standardize provider HTTP request construction and status handling.
   - Current duplication:
     - Repeated `HttpRequestMessage` + bearer setup + status handling across providers.
   - Refactor target:
     - Add provider HTTP helper (`SendGetBearerAsync` style) with consistent timeout/status mapping/logging.

### P1: Eliminate Silent Failures (Logging Rule Compliance)

5. Replace silent catches with logging.
   - Violations:
     - `AIUsageTracker.Web/Services/WebDatabaseService.cs:138` (`catch { }`)
     - `AIUsageTracker.Infrastructure/Providers/OpenAIProvider.cs:246` (`catch { }`)
     - `AIUsageTracker.Infrastructure/Providers/OpenAIProvider.cs:462` (`catch { }`)
     - `AIUsageTracker.Monitor/Program.cs:672` (`catch { }`)
   - Refactor target:
     - Log at `Debug`/`Warning` with context.
     - Keep intentional suppression explicit and documented.

### P2: Extract Shared Auth/JWT/JSON Utilities

6. Consolidate OpenAI and Codex auth parsing logic.
   - Current duplication:
     - JWT decode/claims parsing in both:
       - `AIUsageTracker.Infrastructure/Providers/OpenAIProvider.cs:392`
       - `AIUsageTracker.Infrastructure/Providers/CodexProvider.cs:582`
     - JSON traversal helpers duplicated:
       - `OpenAIProvider.cs:468` (`ReadString` / `ReadDouble`)
       - `CodexProvider.cs:730` (`ReadString` / `ReadDouble`)
     - Auth-file scanning logic duplicated:
       - `OpenAIProvider.cs:208`
       - `CodexProvider.cs:309`
   - Refactor target:
     - `AuthTokenParser` helper for JWT/email/plan extraction.
     - `JsonElementPath` helper for safe `ReadString/ReadDouble/ReadBool`.
     - Shared auth-file locator utility for OpenCode/Codex.

### P2: Split Oversized Service Classes

7. Decompose large classes into focused components.
   - Current sizes:
     - `AIUsageTracker.UI.Slim/App.xaml.cs`: ~1015 lines
     - `AIUsageTracker.Web/Services/WebDatabaseService.cs`: ~963 lines
     - `AIUsageTracker.Monitor/Services/ProviderRefreshService.cs`: ~778 lines
   - Refactor target:
     - `WebDatabaseService` -> `UsageReadService`, `AnalyticsReadService`, `ExportService`, shared mapper.
     - `App.xaml.cs` -> theme service + tray icon service + screenshot service.
     - `ProviderRefreshService` -> refresh orchestration + reset detection + alerting components.

### P2: Remove Web Endpoint Duplication

8. Deduplicate alias endpoints in Web API.
   - Current duplication:
     - `/api/monitor/*` and `/api/agent/*` handlers are duplicated:
       - `AIUsageTracker.Web/Program.cs:146` to `:188`
   - Refactor target:
     - Route mapping helper that registers both aliases to shared delegates.

### P3: Remove Repeated ViewModel Hydration/Preference Boilerplate

9. Centralize provider usage hydration in Web DB layer.
   - Current duplication:
     - Repeated post-query loops applying metadata/display-name:
       - `AIUsageTracker.Web/Services/WebDatabaseService.cs:130`
       - `:176`
       - `:211`
   - Refactor target:
     - Single hydration method for `ProviderUsage` rows.

10. Centralize query/cookie boolean preference handling in Razor pages.
   - Current duplication:
     - `AIUsageTracker.Web/Pages/Index.cshtml.cs:36` to `:99`
   - Refactor target:
     - Shared helper `ReadBoolPreference(queryKey, cookieKey, defaultValue)`.

### P3: Deduplicate Theme Resource Application

11. Replace repetitive theme switch blocks with palette maps.
   - Current duplication:
     - Repeated `SetBrushColor` for each theme in:
       - `AIUsageTracker.UI.Slim/App.xaml.cs:78` onward.
   - Refactor target:
     - Theme palette dictionary keyed by resource name.
     - One loop applies resource colors.

## Suggested Delivery Phases

1. Phase A (P1): Provider registry + error/result normalization + silent catch cleanup.
2. Phase B (P2): Shared auth/json utilities + endpoint dedup + service decomposition start.
3. Phase C (P3): UI/Web boilerplate reductions and theme palette refactor.

## Validation Checklist Per Phase

1. Build solution.
2. Run tests (including startup/deadlock tests).
3. Verify provider list still shows all configured providers (including unavailable).
4. Verify monitor startup serves cached data immediately and only targeted startup refresh runs.
5. Verify no new silent `catch {}` blocks were introduced.
