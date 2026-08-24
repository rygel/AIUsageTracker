# ADR-004: provider_history dedup and compaction

Accepted 2026-03-19. Amended 2026-08-22 (percentage-only cards; `details_json` retired from write/compare).

`StoreHistoryAsync` loads the last row per `(provider_id, card_id)` and inserts only when `IsHistoryUnchanged` is false: `requests_used`, `requests_available`, `requests_percentage` (`UsedPercent`), `is_available`, `status_message`, `next_reset_time`, `http_status`, `name`, `reset_credits_available`, `reset_credit_expirations_utc`. Excluded: `fetched_at`, `response_latency_ms`, `upstream_response_validity`. Unchanged data updates `fetched_at` on the existing row.

`requests_percentage` is compared so percentage-only cards (Grok weekly) are not collapsed. Grok emits `weekly-credits` and `on-demand-credits` under `grok`; the card_id key keeps them independent.

`details_json` is not written or compared (`UsageDatabase` INSERT list). The column stays in migrations/`EnsureColumn` so old values survive.

Daily compaction (`CompactHistoryAsync`, at most once per 23h): last 7 days untouched; 7–90 days last row per hour; older last row per day; `VACUUM` only if rows were deleted. Tests: `UsageDatabaseDedupTests`, `UsageDatabasePipelineTests`, `DatabaseMigrationServiceTests`.
