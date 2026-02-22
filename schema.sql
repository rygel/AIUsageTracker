CREATE TABLE providers (
    provider_id TEXT PRIMARY KEY,
    provider_name TEXT,
    plan_type TEXT,
    auth_source TEXT,
    account_name TEXT,
    updated_at TEXT,
    is_active INTEGER,
    config_json TEXT
);

CREATE TABLE provider_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    provider_id TEXT,
    requests_used REAL,
    requests_available REAL,
    requests_percentage REAL,
    is_available INTEGER,
    status_message TEXT,
    next_reset_time TEXT,
    fetched_at TEXT,
    details_json TEXT
);
