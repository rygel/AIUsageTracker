# AI Usage Tracker - Database Schema

## Overview

AI Usage Tracker uses SQLite with a **four-table design**:

1. **`providers`** - Static provider configuration
2. **`provider_history`** - Time-series usage data (kept indefinitely)
3. **`raw_snapshots`** - Raw JSON data (auto-deleted after 14 days)
4. **`reset_events`** - Quota/limit reset tracking

## Data Preservation Contract

The database design treats usage history as durable product data.

- `providers`, `provider_history`, and `reset_events` are preserved by default.
- Runtime code must not delete these tables' rows unless explicitly approved by the maintainer.
- `raw_snapshots` is the only table with automatic runtime deletion (14-day TTL).
- On tables that participate in foreign keys with `ON DELETE CASCADE`, do not use `INSERT OR REPLACE`; use `INSERT ... ON CONFLICT DO UPDATE` to avoid delete/reinsert side effects.

### Destructive Change Approval Policy

Before introducing any new destructive behavior (`DELETE`, `DROP`, truncation, or retention shrink), require explicit maintainer approval and document:

1. Scope of deleted data
2. Retention window and rationale
3. Rollback/backup strategy

## Database Location

| Platform | Path |
|----------|------|
| Windows | `%LOCALAPPDATA%\AIUsageTracker\Agent\usage.db` |
| Linux | `~/.local/share/AIUsageTracker/Agent/usage.db` |
| macOS | `~/Library/Application Support/AIUsageTracker/Agent/usage.db` |

## Table 1: providers

**Purpose:** Static provider configuration and metadata.

**Retention:** Permanent (until manually deleted).

### Columns

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `provider_id` | TEXT | PRIMARY KEY | Unique identifier (e.g., "openai", "anthropic") |
| `provider_name` | TEXT | NOT NULL | Display name (e.g., "OpenAI") |
| `plan_type` | TEXT | NULL | Plan classification (e.g., usage/coding) |
| `auth_source` | TEXT | NOT NULL | "environment", "auth.json", "manual" |
| `account_name` | TEXT | NULL | Username/email |
| `created_at` | TEXT | NOT NULL | ISO 8601 timestamp |
| `updated_at` | TEXT | NOT NULL | ISO 8601 timestamp |
| `is_active` | INTEGER | NOT NULL | 1 = active, 0 = disabled |
| `config_json` | TEXT | NULL | Additional provider-specific config |

## Table 2: provider_history

**Purpose:** Processed time-series usage data for analytics.

**Retention:** **Indefinite** (kept forever for historical analysis).

### Columns

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `id` | INTEGER | PRIMARY KEY AUTOINCREMENT | Unique row ID |
| `provider_id` | TEXT | NOT NULL, FK → providers | References providers table |
| `requests_percentage` | REAL | NOT NULL | Usage % (0-100) |
| `requests_used` | REAL | NOT NULL | Amount consumed |
| `requests_available` | REAL | NOT NULL | Total/remaining basis depending provider type |
| `is_available` | INTEGER | NOT NULL | 1 = success, 0 = error |
| `status_message` | TEXT | NOT NULL | Human-readable status |
| `next_reset_time` | TEXT | NULL | When quota resets |
| `fetched_at` | TEXT | NOT NULL | ISO 8601 timestamp |
| `details_json` | TEXT | NULL | Optional model/sub-provider details |

### Indexes

```sql
CREATE INDEX idx_history_provider_time 
ON provider_history(provider_id, fetched_at);

CREATE INDEX idx_history_fetched 
ON provider_history(fetched_at);

CREATE INDEX idx_history_fetched_time
ON provider_history(fetched_at DESC);

CREATE INDEX idx_history_provider_id_desc
ON provider_history(provider_id, id DESC);
```

## Table 3: raw_snapshots

**Purpose:** Raw JSON responses from provider APIs (for debugging).

**Retention:** **14 days** (auto-deleted to save space).

### Columns

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `id` | INTEGER | PRIMARY KEY AUTOINCREMENT | Unique row ID |
| `provider_id` | TEXT | NOT NULL | Provider identifier |
| `raw_json` | TEXT | NOT NULL | Complete JSON response |
| `http_status` | INTEGER | NOT NULL | HTTP status code |
| `fetched_at` | TEXT | NOT NULL | ISO 8601 timestamp |

### Auto-Deletion

```sql
-- Runs automatically during each refresh
DELETE FROM raw_snapshots 
WHERE fetched_at < datetime('now', '-14 days');
```

## Table 4: reset_events

**Purpose:** Track quota and limit resets for providers.

**Retention:** **Indefinite** (kept for audit trail).

### Columns

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `id` | INTEGER | PRIMARY KEY AUTOINCREMENT | Unique event ID |
| `provider_id` | TEXT | NOT NULL, FK → providers | References providers table |
| `provider_name` | TEXT | NOT NULL | Display name at time of reset |
| `previous_usage` | REAL | NULL | Usage value before reset |
| `new_usage` | REAL | NULL | Usage value after reset |
| `reset_type` | TEXT | NOT NULL | Type: "monthly", "daily", "manual", "api" |
| `timestamp` | TEXT | NOT NULL | ISO 8601 timestamp of reset |

### Example Events

```sql
-- Monthly quota reset
INSERT INTO reset_events (provider_id, provider_name, previous_usage, new_usage, reset_type, timestamp)
VALUES ('zai', 'Z.AI', 95.5, 0.0, 'monthly', '2024-01-01T00:00:00Z');

-- API-triggered reset
INSERT INTO reset_events (provider_id, provider_name, previous_usage, new_usage, reset_type, timestamp)
VALUES ('openai', 'OpenAI', 100.0, 0.0, 'api', '2024-01-15T12:30:00Z');
```

### Indexes

```sql
CREATE INDEX idx_reset_provider_time
ON reset_events(provider_id, timestamp);

CREATE INDEX idx_reset_timestamp_asc
ON reset_events(timestamp ASC);
```

### Detect Resets

```sql
-- Find all monthly resets in last 30 days
SELECT 
    provider_name,
    reset_type,
    previous_usage,
    new_usage,
    timestamp
FROM reset_events
WHERE reset_type = 'monthly'
  AND timestamp >= datetime('now', '-30 days')
ORDER BY timestamp DESC;
```

## Relationships

```
┌─────────────┐         ┌──────────────────┐
│  providers  │◄────────┤ provider_history │
│  (static)   │  1:N    │   (analytics)    │
└──────┬──────┘         └──────────────────┘
       │                         ▲
       │                         │
       │                ┌────────┴─────────┐
       │                │ raw_snapshots    │
       │                │  (14-day TTL)    │
       │                └──────────────────┘
       │
       │         ┌──────────────────┐
       └────────►│  reset_events    │
              1:N│  (audit trail)   │
                 └──────────────────┘
```

## Example Queries

### Get latest data for all providers
```sql
SELECT 
    p.provider_name,
    h.usage_percentage,
    h.cost_used,
    h.cost_limit,
    h.status_message,
    h.fetched_at
FROM providers p
JOIN provider_history h ON p.provider_id = h.provider_id
WHERE h.id IN (
    SELECT MAX(id) 
    FROM provider_history 
    GROUP BY provider_id
)
ORDER BY p.provider_name;
```

### Get provider history
```sql
SELECT * FROM provider_history 
WHERE provider_id = 'openai'
ORDER BY fetched_at DESC
LIMIT 100;
```

### Get raw data for debugging
```sql
SELECT raw_json, http_status, fetched_at
FROM raw_snapshots
WHERE provider_id = 'anthropic'
  AND fetched_at >= datetime('now', '-1 day')
ORDER BY fetched_at DESC;
```

### Daily cost analysis
```sql
SELECT 
    p.provider_name,
    date(h.fetched_at) as day,
    MIN(h.cost_used) as start_cost,
    MAX(h.cost_used) as end_cost,
    MAX(h.cost_used) - MIN(h.cost_used) as daily_cost
FROM providers p
JOIN provider_history h ON p.provider_id = h.provider_id
WHERE h.fetched_at >= datetime('now', '-7 days')
GROUP BY p.provider_id, date(h.fetched_at)
ORDER BY day DESC, p.provider_name;
```

## API Endpoints

### Get Latest Data
```
GET /api/usage
```
Returns current snapshot for all providers.

### Get Historical Data
```
GET /api/history?limit=100
```
Returns processed history from `provider_history`.

### Get Raw Data
```
GET /api/raw/{providerId}?limit=50
```
Returns raw JSON snapshots (last 14 days only).

## Runtime Tuning (SQLite)

The Monitor applies runtime SQLite pragmas at migration/startup:

- `PRAGMA journal_mode=WAL`
- `PRAGMA synchronous=NORMAL`
- `PRAGMA busy_timeout=5000`
- `PRAGMA temp_store=MEMORY`
- `PRAGMA foreign_keys=ON`

The Web UI uses pooled shared-cache connections for read-heavy queries and adds short-lived memory caches for hot reads.

## Configuration Files

**auth.json** - Provider credentials:
```json
{
  "openai": { "api_key": "sk-..." },
  "anthropic": { "api_key": "sk-ant-..." }
}
```

**preferences.json** - UI settings:
```json
{
  "always_on_top": true,
  "compact_mode": true
}
```
