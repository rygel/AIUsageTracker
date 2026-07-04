-- Add reset_credits_available to provider_history for Codex rate_limit_reset_credits.available_count.
-- Stores the number of manual rate-limit resets the user still has available.
-- Null for providers that do not report it; preserved indefinitely as part of usage history.
ALTER TABLE provider_history ADD COLUMN reset_credits_available INTEGER;
