-- Add parent_provider_id to provider_history
-- Null for top-level providers; non-null for child/derived rows (e.g. "antigravity.gpt-4o").
ALTER TABLE provider_history ADD COLUMN parent_provider_id TEXT REFERENCES providers(provider_id) ON DELETE SET NULL;
CREATE INDEX IF NOT EXISTS idx_history_parent_provider ON provider_history(parent_provider_id);

-- Recreate raw_snapshots with a proper FK on provider_id (ON DELETE CASCADE).
-- SQLite does not support ALTER TABLE ADD CONSTRAINT, so we use the create/insert/drop/rename pattern.
CREATE TABLE raw_snapshots_new (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    provider_id TEXT NOT NULL REFERENCES providers(provider_id) ON DELETE CASCADE,
    raw_json TEXT NOT NULL,
    http_status INTEGER NOT NULL DEFAULT 200,
    fetched_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Copy rows that have a matching provider; drop orphaned debug snapshots.
INSERT INTO raw_snapshots_new SELECT * FROM raw_snapshots
    WHERE provider_id IN (SELECT provider_id FROM providers);

DROP TABLE raw_snapshots;
ALTER TABLE raw_snapshots_new RENAME TO raw_snapshots;

CREATE INDEX IF NOT EXISTS idx_raw_fetched ON raw_snapshots(fetched_at);
CREATE INDEX IF NOT EXISTS idx_raw_provider ON raw_snapshots(provider_id);
