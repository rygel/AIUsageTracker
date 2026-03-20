-- Migrate fetched_at from TEXT (ISO 8601) to INTEGER (Unix epoch seconds) in
-- provider_history and raw_snapshots. SQLite does not support ALTER COLUMN, so
-- the standard 12-step table-rebuild procedure is used.
--
-- Savings: ~15 bytes per row (19-char ISO string → 4-byte integer).
-- With the dedup gate in place this is largely a one-time compaction benefit;
-- new rows continue to use compact integer storage going forward.

PRAGMA foreign_keys = OFF;

-- ── provider_history ─────────────────────────────────────────────────────────

CREATE TABLE provider_history_new (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    provider_id TEXT NOT NULL,
    is_available INTEGER NOT NULL DEFAULT 1,
    status_message TEXT NOT NULL DEFAULT '',
    next_reset_time TEXT,
    requests_used REAL NOT NULL DEFAULT 0,
    requests_available REAL NOT NULL DEFAULT 0,
    requests_percentage REAL NOT NULL DEFAULT 0,
    response_latency_ms REAL NOT NULL DEFAULT 0,
    http_status INTEGER NOT NULL DEFAULT 0,
    upstream_response_validity INTEGER NOT NULL DEFAULT 0,
    upstream_response_note TEXT NOT NULL DEFAULT '',
    fetched_at INTEGER NOT NULL DEFAULT (strftime('%s', 'now')),
    details_json TEXT,
    parent_provider_id TEXT REFERENCES providers(provider_id) ON DELETE SET NULL,
    FOREIGN KEY (provider_id) REFERENCES providers(provider_id) ON DELETE CASCADE
);

INSERT INTO provider_history_new
SELECT id, provider_id, is_available, status_message, next_reset_time,
       requests_used, requests_available, requests_percentage,
       response_latency_ms, http_status, upstream_response_validity, upstream_response_note,
       CAST(strftime('%s', fetched_at) AS INTEGER),
       details_json, parent_provider_id
FROM provider_history;

DROP TABLE provider_history;
ALTER TABLE provider_history_new RENAME TO provider_history;

-- ── raw_snapshots ─────────────────────────────────────────────────────────────

CREATE TABLE raw_snapshots_new (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    provider_id TEXT NOT NULL REFERENCES providers(provider_id) ON DELETE CASCADE,
    raw_json TEXT NOT NULL,
    http_status INTEGER NOT NULL DEFAULT 200,
    fetched_at INTEGER NOT NULL DEFAULT (strftime('%s', 'now'))
);

INSERT INTO raw_snapshots_new
SELECT id, provider_id, raw_json, http_status,
       CAST(strftime('%s', fetched_at) AS INTEGER)
FROM raw_snapshots;

DROP TABLE raw_snapshots;
ALTER TABLE raw_snapshots_new RENAME TO raw_snapshots;

PRAGMA foreign_keys = ON;

-- ── rebuild indexes ───────────────────────────────────────────────────────────

CREATE INDEX IF NOT EXISTS idx_history_provider_time ON provider_history(provider_id, fetched_at);
CREATE INDEX IF NOT EXISTS idx_raw_fetched ON raw_snapshots(fetched_at);
CREATE INDEX IF NOT EXISTS idx_history_fetched_time ON provider_history(fetched_at DESC);
CREATE INDEX IF NOT EXISTS idx_history_provider_id_desc ON provider_history(provider_id, id DESC);
CREATE INDEX IF NOT EXISTS idx_history_is_available ON provider_history(is_available);
CREATE INDEX IF NOT EXISTS idx_history_provider_fetched_desc ON provider_history(provider_id, fetched_at DESC);
