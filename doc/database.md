# AI Consumption Tracker - Database Schema

## Overview

AI Consumption Tracker uses SQLite with a **three-table design**:

1. **`providers`** - Static provider configuration
2. **`provider_history`** - Time-series usage data (kept indefinitely)
3. **`raw_snapshots`** - Raw JSON data (auto-deleted after 14 days)

## Database Location

| Platform | Path |
|----------|------|
| Windows | `%LOCALAPPDATA%\AIConsumptionTracker\Agent\usage.db` |
| Linux | `~/.local/share/AIConsumptionTracker/Agent/usage.db` |
| macOS | `~/Library/Application Support/AIConsumptionTracker/Agent/usage.db` |

## Table 1: providers

**Purpose:** Static provider configuration and metadata.

**Retention:** Permanent (until manually deleted).

### Columns

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `provider_id` | TEXT | PRIMARY KEY | Unique identifier (e.g., "openai", "anthropic") |
| `provider_name` | TEXT | NOT NULL | Display name (e.g., "OpenAI") |
| `payment_type` | TEXT | NOT NULL | "usage_based", "credits", or "quota" |
| `api_key` | TEXT | NULL | Encrypted API key (optional) |
| `base_url` | TEXT | NULL | Custom API endpoint |
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
| `usage_percentage` | REAL | NOT NULL | Usage % (0-100) |
| `cost_used` | REAL | NOT NULL | Amount consumed |
| `cost_limit` | REAL | NOT NULL | Total budget/limit |
| `is_available` | INTEGER | NOT NULL | 1 = success, 0 = error |
| `status_message` | TEXT | NOT NULL | Human-readable status |
| `next_reset_time` | TEXT | NULL | When quota resets |
| `fetched_at` | TEXT | NOT NULL | ISO 8601 timestamp |

### Indexes

```sql
CREATE INDEX idx_history_provider_time 
ON provider_history(provider_id, fetched_at);

CREATE INDEX idx_history_fetched 
ON provider_history(fetched_at);
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

## Relationships

```
┌─────────────┐         ┌──────────────────┐
│  providers  │◄────────┤ provider_history │
│  (static)   │  1:N    │   (analytics)    │
└─────────────┘         └──────────────────┘
                                ▲
                                │
                         ┌──────┴─────────┐
                         │ raw_snapshots  │
                         │  (14-day TTL)  │
                         └────────────────┘
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
