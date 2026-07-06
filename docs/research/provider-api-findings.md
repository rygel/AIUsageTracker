# Provider API Investigation Findings (Cycle 22)

**Date:** 2026-06-26
**Task:** task-102 — Investigate candidate provider APIs for usage/quota endpoints

## Summary

Investigated three provider APIs for usage/quota/billing endpoints. One provider (Groq) was implemented; two were documented as not viable for the tracker.

| Provider | Endpoint Exists? | Auth Required | Data Available | Verdict |
|----------|-----------------|---------------|----------------|---------|
| Google Gemini | No (standard API) | API key | None for standard API key path | Already implemented (OAuth CLI quota) |
| Anthropic Claude | Yes (Usage & Cost API) | Admin API key | Spending, tokens per model, workspace breakdowns | Not viable — requires admin key, most users only have standard API key |
| Groq | No (rate-limit headers only) | Standard API key | Remaining requests/day, tokens/minute | **Implemented** as rate-limit-header provider |

---

## 1. Google Gemini (`generativelanguage.googleapis.com`)

### Investigation

The standard Gemini API at `generativelanguage.googleapis.com` provides model inference endpoints only (generateContent, embedContent, listModels, etc.). There is **no usage, quota, or billing endpoint** accessible via a standard API key.

Google provides quota information through:
- **Google Cloud Console** (web UI) — requires GCP project access
- **Service Usage API** (`serviceusage.googleapis.com`) — requires GCP OAuth scopes, not API keys

### Existing Implementation

The project already has a `GeminiProvider` (`gemini-cli` provider ID) that uses OAuth credentials from the gemini-cli tool to query `cloudcode-pa.googleapis.com/v1internal:retrieveUserQuota`. This is a completely different auth path from the standard API key — it uses embedded OAuth client credentials from the open-source CLI tool.

### Verdict

**Already implemented.** The OAuth-based quota path is the only way to get Gemini quota data. A new API-key-based provider has no viable endpoint.

---

## 2. Anthropic Claude (`api.anthropic.com`)

### Investigation

Anthropic offers two distinct APIs for usage data:

#### Standard API (Rate-Limit Headers)

Every response to `POST /v1/messages` includes rate-limit headers:
- `anthropic-ratelimit-requests-limit` / `anthropic-ratelimit-requests-remaining`
- `anthropic-ratelimit-tokens-limit` / `anthropic-ratelimit-tokens-remaining`
- `anthropic-ratelimit-input-tokens-limit` / `anthropic-ratelimit-input-tokens-remaining`
- `anthropic-ratelimit-output-tokens-limit` / `anthropic-ratelimit-output-tokens-remaining`

These are instantaneous rate-limit capacity, not cumulative usage. The existing `ClaudeCodeProvider` already reads these headers in its API-key fallback path.

#### Admin API (Usage & Cost API)

Anthropic provides an excellent Usage and Cost Admin API:
- `GET /v1/organizations/usage_report/messages` — token counts per model, per workspace, per service tier, with time bucketing (1m/1h/1d)
- `GET /v1/organizations/cost_report` — USD cost breakdowns by workspace, model, and description

This provides exactly the data the tracker is designed for (spending, token usage, budget tracking).

**However**, this API requires an **Admin API key** (`sk-ant-admin01-...`), which is:
- Different from the standard Claude API key (`sk-ant-api...`)
- Only available to organization admins who set up their org in the Console
- Not discoverable via the standard `ANTHROPIC_API_KEY` environment variable

### Verdict

**Not viable for implementation.** The Usage & Cost API has the clearest usage data of all three providers, but the admin key requirement creates a significant barrier for typical users. The standard API key path is already handled by `ClaudeCodeProvider`. Adding a separate provider that requires admin keys would be confusing and most users would see "API Key missing."

**Future consideration:** If demand exists, a separate `anthropic-admin` provider could be added that accepts admin keys and queries the Usage & Cost API. This would require explicit user configuration (no auto-discovery).

---

## 3. Groq (`api.groq.com`)

### Investigation

Groq provides an OpenAI-compatible API. There is **no dedicated usage or billing endpoint**.

However, every response includes rate-limit headers:
- `x-ratelimit-limit-requests` — requests per day (RPD) limit
- `x-ratelimit-remaining-requests` — remaining requests today
- `x-ratelimit-reset-requests` — time until daily limit resets (Go duration format)
- `x-ratelimit-limit-tokens` — tokens per minute (TPM) limit
- `x-ratelimit-remaining-tokens` — remaining tokens this minute
- `x-ratelimit-reset-tokens` — time until token limit resets

### Authentication

Standard API key via `Authorization: Bearer <key>`. Discoverable via `GROQ_API_KEY` environment variable.

### Data Available

Rate-limit headers provide instantaneous capacity data:
- Daily request quota: used/remaining with reset time
- Per-minute token quota: used/remaining with reset time

This is not cumulative usage or spending — it's "how much capacity do I have right now." Similar to what the xAI provider shows (key status) and what ClaudeCodeProvider shows in its API-key path.

### Verdict

**Implemented as `GroqProvider`.** The provider:
- GETs `/openai/v1/models` (lightweight endpoint) with Bearer auth
- Reads rate-limit headers from the response
- Emits two cards: Daily Requests and Per-Minute Tokens
- Falls back to a status-only "Connected" card if headers are missing
- Auto-discovered via `GROQ_API_KEY` environment variable
- Follows ProviderBase pattern (AD-8) for error handling

---

## Decision Rationale

Groq was chosen for implementation because:
1. **Standard API key** — no OAuth, no admin key, works with existing discovery patterns
2. **No existing provider** — clean addition with no overlap
3. **Rate-limit data is useful** — shows current capacity, more informative than OpenAI's API-key path which only says "Connected"
4. **Follows established patterns** — xAI and ClaudeCodeProvider both use non-usage endpoints to show status data

Anthropic was not implemented because:
- The Usage & Cost API requires admin keys most users won't have
- The standard API key path is already handled by ClaudeCodeProvider
- Adding a confusing second Anthropic provider entry in settings would hurt UX

Gemini was not implemented because:
- The existing GeminiProvider already covers the only viable quota endpoint (OAuth CLI path)
- The standard API has no usage endpoint at all
