# ADR-004: provider_history Write Deduplication and Periodic Compaction

## Status
Accepted — 2026-03-19

## Context

The monitor refreshes every ~5 minutes across ~25 providers. Each refresh unconditionally inserted a new row into `provider_history` regardless of whether anything had changed. At this rate the table grew to ~30,000 rows in 19 days, consuming ~104 MB — approximately 5.5 MB/day. Left unchecked, the file would exceed 1 GB within a year.

The vast majority of rows were exact duplicates: during idle periods (nights, weekends) a provider's quota does not change, so consecutive rows were bitwise identical except for `fetched_at` and `response_latency_ms`.

Two complementary mechanisms were introduced to address this.

## Decision

### Mechanism 1: Write Deduplication Gate (primary)

Before inserting a new row for a provider, `StoreHistoryAsync` loads the most recent stored row for each incoming provider in a single batched SELECT query and compares the following **meaningful** fields:

| Field | Rationale |
|---|---|
| `requests_used` | Core quota metric |
| `requests_available` | Core quota metric |
| `is_available` | Availability flip always matters |
| `status_message` | Error messages are meaningful state |
| `next_reset_time` | Reset timestamp change must be recorded |
| `details_json` | Sub-quota windows (spark, daily, etc.) |
| `http_status` | 200 → 429 / 5xx is a meaningful state change |

The following fields are **excluded** from comparison because they change on every fetch but carry no state information:

| Field | Reason excluded |
|---|---|
| `fetched_at` | Trivially different every poll |
| `response_latency_ms` | Network noise, not quota state |
| `requests_percentage` | Derived from used/available |
| `upstream_response_validity` | Derived classification |

**When data is unchanged**: instead of skipping the update entirely, the existing row's `fetched_at` is updated to the current timestamp via `UPDATE provider_history SET fetched_at = @now WHERE id = @lastRowId`. This keeps the stale-data detector (which reads `fetched_at` of the last row) seeing a fresh timestamp even though no new row was written.

**When data has changed**: a normal INSERT is performed, preserving the full history of every meaningful state transition.

### Mechanism 2: Periodic Compaction (safety valve)

Even with the dedup gate in place, intensive-use periods (where quota genuinely ticks every 5 minutes) still generate one row per poll. A daily compaction pass downsamples these into a bounded set:

| Age window | Kept resolution |
|---|---|
| Last 7 days | Full resolution (~5 min) — untouched |
| 7 – 90 days | One row per hour per provider |
| Older than 90 days | One row per day per provider |

The compaction keeps `MAX(id)` for each bucket (the most recent reading of the period). A `VACUUM` is run immediately after to reclaim freed pages and shrink the file on disk. The compaction runs at most once per day, tracked by an in-memory `_lastCompactedAt` timestamp that resets to `DateTime.MinValue` on app restart (ensuring it fires on the first startup after a restart).

## Why `UPDATE fetched_at` instead of simply skipping

The stale-data detection in `GetLatestHistoryAsync` checks whether the most recent row's `fetched_at` is older than one hour. If we only skip the INSERT with no side-effect, a stable provider that hasn't changed for two hours would appear stale to the UI even though the monitor has been polling it successfully every five minutes. Touching `fetched_at` preserves the invariant: **`fetched_at` of the last row always reflects the last successful poll, not the last data change**.

## Why both mechanisms

The dedup gate prevents new redundant data from being written — it is the primary, proactive control. The compaction handles:

1. **Existing data** accumulated before the dedup gate was deployed.
2. **Intensive-use periods** where quota genuinely changes every poll (e.g., a bulk run that burns through requests).
3. **Defense in depth** — if a provider returns slightly different floating-point values on every call (pathological case), the compaction still bounds the row count over time.

## Consequences

- `provider_history` row growth drops from ~1,500–3,000 rows/day to a rate proportional to actual quota state changes. During idle overnight periods this approaches zero new rows.
- `fetched_at` on the most recent row now means "last confirmed current" rather than "first time this exact data was seen". Queries that interpret `fetched_at` as the original observation timestamp should be aware of this; for all current uses (stale detection, latest-row queries) the semantics are correct.
- The dedup gate adds one batched SELECT per `StoreHistoryAsync` call. With 25 providers and the `idx_history_provider_id_desc` index this is negligible.
- Compaction requires an exclusive lock for the `VACUUM` step. With the `SemaphoreSlim` guard in `UsageDatabase` all concurrent DB operations in this process are serialised, so `VACUUM` can acquire the lock cleanly. The `busy_timeout = 5000ms` pragma handles any external readers.
- History charts that rely on dense time-series data should use `GetRecentHistoryAsync` (bounded by count per provider) rather than assumptions about row density.
- Test coverage: `ProviderRefreshServiceTests` (mock-based, auto-stubs `CompactHistoryAsync`). Integration coverage via `DatabaseMigrationServiceTests` which exercises the full DB path.
