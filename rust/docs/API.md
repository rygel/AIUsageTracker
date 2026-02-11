# AI Consumption Tracker - Agent API Documentation

## Overview

The Agent provides an HTTP API on port 8080 that the UI (Tauri app) uses to fetch provider data, usage information, and configuration. The UI is fully dependent on the Agent - all data comes from the Agent's API.

**Base URL:** `http://localhost:8080`

---

## Endpoints

### Health Check

**GET** `/health`

Returns 200 OK if the agent is running.

**Response:**
- Status: 200 OK
- Body: Empty

**Used by:** UI to check if agent is alive (via `is_agent_running_http` command)

---

### Get Current Usage

**GET** `/api/providers/usage`

Returns current usage data for all configured providers.

**Response:**
```json
[
  {
    "provider_id": "openai",
    "provider_name": "OpenAI",
    "usage_percentage": 45.5,
    "cost_used": 45.50,
    "cost_limit": 100.00,
    "payment_type": "payg",
    "usage_unit": "USD",
    "is_quota_based": false,
    "is_available": true,
    "description": "Organization: MyOrg",
    "auth_source": "Environment Variable",
    "account_name": "my@email.com",
    "next_reset_time": null,
    "details": null
  }
]
```

**Used by:** UI main window to display usage data (via `get_usage_from_agent` command)

**Notes:**
- Returns data from the agent's in-memory provider manager
- Does not trigger a refresh - returns cached data

---

### Refresh Usage

**POST** `/api/providers/usage/refresh`

Triggers a fresh fetch from all provider APIs and returns the updated data.

**Response:** Same as `/api/providers/usage`

**Used by:** UI refresh button (via `refresh_usage_from_agent` command)

**Notes:**
- Fetches fresh data from provider APIs
- Stores results in the database
- May take several seconds depending on provider response times

---

### Get Provider Usage

**GET** `/api/providers/{provider_id}/usage`

Returns usage data for a specific provider.

**Parameters:**
- `provider_id` (path): Provider identifier (e.g., "openai", "anthropic")

**Response:**
```json
{
  "provider_id": "openai",
  "provider_name": "OpenAI",
  "usage_percentage": 45.5,
  "cost_used": 45.50,
  "cost_limit": 100.00,
  "payment_type": "payg",
  "usage_unit": "USD",
  "is_quota_based": false,
  "is_available": true,
  "description": "Organization: MyOrg",
  "auth_source": "Environment Variable",
  "account_name": "my@email.com",
  "next_reset_time": null,
  "details": null
}
```

**Status Codes:**
- 200: Success
- 404: Provider not found

---

### Get Discovered Providers

**GET** `/api/providers/discovered`

Returns all providers discovered by the agent (from environment variables, config files, etc.).

**Response:**
```json
[
  {
    "provider_id": "openai",
    "api_key": "sk-...",
    "config_type": "api",
    "description": "Discovered via Environment Variable",
    "auth_source": "Environment Variable",
    "show_in_tray": true
  }
]
```

**Used by:** Settings dialog (via `get_all_providers_from_agent` command)

**Notes:**
- Agent discovers providers from:
  - Environment variables (OPENAI_API_KEY, ANTHROPIC_API_KEY, etc.)
  - Existing config files (~/.ai-consumption-tracker/auth.json)
  - Well-known provider defaults
- Includes providers even if API key is not set

---

### Get Historical Usage

**GET** `/api/history`

Returns historical usage data from the database.

**Query Parameters:**
- `provider_id` (optional): Filter by specific provider
- `start_date` (optional): ISO 8601 date (e.g., "2024-01-01")
- `end_date` (optional): ISO 8601 date (e.g., "2024-12-31")
- `limit` (optional): Maximum records to return (default: 100)

**Response:**
```json
[
  {
    "id": "uuid",
    "provider_id": "openai",
    "provider_name": "OpenAI",
    "usage": 45.50,
    "limit": 100.00,
    "usage_unit": "USD",
    "is_quota_based": false,
    "timestamp": "2024-01-15T10:30:00Z"
  }
]
```

---

### Get Config

**GET** `/api/config`

Returns the agent's configuration.

**Response:**
```json
{
  "refresh_interval_minutes": 5,
  "auto_refresh_enabled": true,
  "discovered_providers": [
    {
      "provider_id": "openai",
      "api_key": "sk-...",
      "config_type": "api",
      "description": "Discovered via Environment Variable",
      "auth_source": "Environment Variable",
      "show_in_tray": true
    }
  ]
}
```

---

### Update Config

**POST** `/api/config`

Updates the agent's configuration.

**Request Body:**
```json
{
  "refresh_interval_minutes": 10,
  "auto_refresh_enabled": true
}
```

**Response:** Returns updated config

**Notes:**
- Only `refresh_interval_minutes` and `auto_refresh_enabled` can be updated
- Provider list is auto-discovered and cannot be manually set via this endpoint

---

## Data Types

### ProviderUsage

The main data structure returned by usage endpoints.

```rust
pub struct ProviderUsage {
    pub provider_id: String,
    pub provider_name: String,
    pub usage_percentage: f64,      // 0-100 percentage
    pub cost_used: f64,             // Amount used
    pub cost_limit: f64,            // Limit (0 if unlimited)
    pub payment_type: PaymentType,  // "payg" | "quota" | "credits"
    pub usage_unit: String,         // "USD", "requests", "tokens"
    pub is_quota_based: bool,       // True if quota-based billing
    pub is_available: bool,         // True if API call succeeded
    pub description: String,        // Additional info (org name, etc.)
    pub auth_source: String,        // Where credentials came from
    pub account_name: String,       // Account/org identifier
    pub next_reset_time: Option<DateTime<Utc>>,
    pub details: Option<Vec<ProviderUsageDetail>>,
}
```

### ProviderConfig

Configuration for a provider.

```rust
pub struct ProviderConfig {
    pub provider_id: String,
    pub api_key: String,
    pub config_type: String,        // "api" | "quota" | "pay-as-you-go"
    pub description: Option<String>,
    pub auth_source: String,        // "Environment Variable", "Config File"
    pub show_in_tray: bool,
}
```

### PaymentType

Enum representing payment models:
- `payg` - Pay-as-you-go
- `quota` - Quota-based (fixed limit)
- `credits` - Credit-based system

---

## Error Handling

All endpoints return standard HTTP status codes:

- **200 OK** - Request succeeded
- **404 Not Found** - Provider not found
- **500 Internal Server Error** - Server error
- **503 Service Unavailable** - Agent not ready (e.g., database not initialized)

Error responses include a plain text error message.

---

## UI Integration

### Commands (Tauri)

The UI invokes these commands which call the Agent API:

1. **`get_usage_from_agent()`** → `GET /api/providers/usage`
2. **`refresh_usage_from_agent()`** → `POST /api/providers/usage/refresh`
3. **`get_all_providers_from_agent()`** → `GET /api/providers/discovered`
4. **`is_agent_running_http()`** → `GET /health`

### UI Status Flow

1. UI starts → Shows "Waiting for agent..."
2. `checkAgentStatus()` polls `/health` every 5 seconds
3. When agent responds:
   - Footer shows "Agent Connected" (green LED)
   - 🤖 Button disabled
   - Content area loads data from `/api/providers/usage`
4. When agent stops:
   - Footer shows "Agent Disconnected" (red LED)
   - 🤖 Button enabled
   - Content shows "Waiting for agent..."

### Provider Discovery Flow

1. Agent starts
2. Scans environment variables (OPENAI_API_KEY, etc.)
3. Checks existing config files
4. Discovers well-known providers
5. Stores in `discovered_providers` list
6. UI fetches via `/api/providers/discovered`
7. Settings dialog displays discovered providers

---

## Provider Discovery

The agent automatically discovers providers from:

### Environment Variables
- `OPENAI_API_KEY` → openai
- `ANTHROPIC_API_KEY` / `CLAUDE_API_KEY` → claude-code
- `GEMINI_API_KEY` / `GOOGLE_API_KEY` → gemini-cli
- `DEEPSEEK_API_KEY` → deepseek
- `KIMI_API_KEY` / `MOONSHOT_API_KEY` → kimi

### Config Files
- `~/.ai-consumption-tracker/auth.json`
- `~/.local/share/opencode/auth.json`

### Well-Known Providers
Always included with empty API keys:
- openai
- minimax
- xiaomi
- kimi
- kilocode
- claude-code
- gemini-cli
- antigravity
- deepseek
- openrouter
- zai

---

## Development Notes

### Adding New Endpoints

1. Add route in `main.rs`:
```rust
.route("/api/new-endpoint", get(new_endpoint_handler))
```

2. Implement handler:
```rust
async fn new_endpoint_handler(
    State(state): State<AppState>,
) -> Result<Json<SomeType>, StatusCode> {
    // Implementation
    Ok(Json(result))
}
```

3. Add Tauri command in `commands.rs`:
```rust
#[tauri::command]
pub async fn new_command() -> Result<SomeType, String> {
    match reqwest::get("http://localhost:8080/api/new-endpoint").await {
        Ok(response) => response.json().await.map_err(|e| e.to_string()),
        Err(e) => Err(format!("Agent error: {}", e)),
    }
}
```

4. Register in `main.rs`:
```rust
.invoke_handler(tauri::generate_handler![
    // ... other commands
    new_command,
])
```

### Testing Endpoints

Use curl to test:
```bash
# Health check
curl http://localhost:8080/health

# Get usage
curl http://localhost:8080/api/providers/usage | jq

# Refresh usage
curl -X POST http://localhost:8080/api/providers/usage/refresh | jq

# Get discovered providers
curl http://localhost:8080/api/providers/discovered | jq
```

---

## Security Considerations

- API is bound to `127.0.0.1:8080` (localhost only)
- No authentication required (local access only)
- API keys are stored in agent memory and database
- Config endpoint exposes API keys - use only locally

---

## Version History

### Current (v1.7.13)
- All endpoints return `ProviderUsage` from aic_core
- Added `/api/providers/discovered` endpoint
- Agent-only architecture (UI requires agent)
- Harmonized UI status updates

### Previous
- Used custom `UsageResponse` type (incompatible with UI)
- UI had local config fallback
