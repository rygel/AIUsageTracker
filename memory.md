# AI Usage Tracker - Memory State

## Current Status

- Branch: `feature/web-ui-dashboard-check`
- PR: https://github.com/rygel/AIConsumptionTracker/pull/130
- Working focus: naming migration (`AIConsumptionTracker` -> `AIUsageTracker`) plus Web performance hardening

## Current State Variant

- Variant: **rename-complete-with-web-perf-hardening**
- Naming policy:
  - User-facing terminology is `Monitor` and `AI Usage`.
  - Executable/artifact naming uses `AIUsageTracker*`.
  - Compatibility fallbacks for legacy `AIConsumptionTracker` paths are preserved where needed.
- UI policy:
  - Antigravity must not silently degrade to a healthy-looking parent fallback when model details are missing.
  - Missing model-level data must show explicit actionable messaging.
- Web policy:
  - Inactive providers are hidden by default on dashboard (toggle available).
  - Charts prioritize fast first paint; non-critical reset events can load after initial chart render.

## Recent Commits (Newest First)

- `e4a4a6c` perf(web): add DB telemetry, hot read cache, and CI perf smoke
- `155bcae` perf(sqlite): tune pragmas and shared-cache connection settings
- `ba23d6b` perf(web): add output caching and chart downsampling
- `017bffa` perf(web): add response compression and parallel dashboard queries
- `5963aa0` perf(web): cache chart colors and filter active providers in SQL
- `f195d3c` perf(web): speed up charts queries and default hide inactive
- `e2f72e6` test(ui): sync screenshot baselines with CI renderer
- `99e9e2f` fix(ci): update release checks and screenshot baselines

## What Was Updated in This Variant

### Naming and Runtime Compatibility

- Project folders and namespaces migrated to `AIUsageTracker.*`.
- Solution/project filenames and published executable names migrated to `AIUsageTracker*`.
- Runtime lookup and storage paths updated to new naming with legacy fallback reads/writes.

### Agent/Monitor Behavior

- Monitor startup/migration logic now tolerates pre-existing legacy SQLite schemas.
- Startup flow forces immediate Antigravity refresh to avoid stale model-quota presentation.

### Web Performance

- Response compression enabled (Brotli/Gzip).
- Static assets served with cache headers.
- Dashboard and Charts output caching added.
- Chart data query made index-friendly and downsampled by time bucket.
- Hot DB reads cached in-memory (short TTL).
- DB telemetry added for key read paths (elapsed ms + row counts).
- CI includes web endpoint perf smoke guardrail for `/` and `/charts`.

## Key Files Touched Recently

- `AIUsageTracker.Web/Program.cs`
- `AIUsageTracker.Web/Services/WebDatabaseService.cs`
- `AIUsageTracker.Web/Pages/Index.cshtml`
- `AIUsageTracker.Web/Pages/Index.cshtml.cs`
- `AIUsageTracker.Web/Pages/Charts.cshtml`
- `AIUsageTracker.Web/Pages/Charts.cshtml.cs`
- `AIUsageTracker.Monitor/Services/DatabaseMigrationService.cs`
- `AIUsageTracker.Monitor/Services/UsageDatabase.cs`
- `.github/workflows/test.yml`

## Known Workspace Note

- Untracked file `nul` exists at repo root. Avoid broad root operations that may touch it.
