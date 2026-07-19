-- Add reset_credit_expirations_utc to provider_history for Codex rate_limit_reset_credits per-reset expirations.
-- Stores one ISO 8601 UTC timestamp per available reset credit, serialized as a JSON array of ticks.
-- Null for providers that do not report per-reset expirations; preserved indefinitely as part of usage history.
ALTER TABLE provider_history ADD COLUMN reset_credit_expirations_utc TEXT;