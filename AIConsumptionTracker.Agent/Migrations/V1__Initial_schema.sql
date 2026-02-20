-- Initial schema for AIConsumptionTracker Agent
-- Creates all 4 tables: providers, provider_history, raw_snapshots, reset_events

-- Table 1: providers - Static provider configuration
CREATE TABLE providers (
    provider_id TEXT PRIMARY KEY,
    provider_name TEXT NOT NULL,
    account_name TEXT,
    created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    is_active INTEGER NOT NULL DEFAULT 1,
    config_json TEXT
);

-- Table 2: provider_history - Time-series usage data (kept indefinitely)
CREATE TABLE provider_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    provider_id TEXT NOT NULL,
    is_available INTEGER NOT NULL DEFAULT 1,
    status_message TEXT NOT NULL DEFAULT '',
    next_reset_time TEXT,
    requests_used INTEGER NOT NULL,
    requests_available INTEGER NOT NULL,
    requests_percentage INTEGER NOT NULL,
    fetched_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (provider_id) REFERENCES providers(provider_id) ON DELETE CASCADE
);

-- Index for provider_history
CREATE INDEX idx_history_provider_time 
ON provider_history(provider_id, fetched_at);

-- Table 3: raw_snapshots - Raw JSON data (14-day TTL)
CREATE TABLE raw_snapshots (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    provider_id TEXT NOT NULL,
    raw_json TEXT NOT NULL,
    http_status INTEGER NOT NULL DEFAULT 200,
    fetched_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

-- Index for raw_snapshots
CREATE INDEX idx_raw_fetched 
ON raw_snapshots(fetched_at);

-- Table 4: reset_events - Quota/limit reset tracking (kept indefinitely)
CREATE TABLE reset_events (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    provider_id TEXT NOT NULL,
    provider_name TEXT NOT NULL,
    previous_usage REAL,
    new_usage REAL,
    reset_type TEXT NOT NULL,
    timestamp TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (provider_id) REFERENCES providers(provider_id) ON DELETE CASCADE
);

-- Index for reset_events
CREATE INDEX idx_reset_provider_time 
ON reset_events(provider_id, timestamp);
