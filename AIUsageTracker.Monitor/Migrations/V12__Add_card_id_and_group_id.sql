-- Add card_id and group_id to provider_history for flat-card model.
-- card_id: stable identifier for a specific card within a provider (e.g. "current-session", "sonnet").
-- group_id: rendering grouping tag — cards sharing a group_id are displayed together.
-- Both are nullable; null means single-card provider (legacy behaviour, default card).
ALTER TABLE provider_history ADD COLUMN card_id TEXT;
ALTER TABLE provider_history ADD COLUMN group_id TEXT;

-- Index to support efficient dedup queries using the composite (provider_id, card_id) key.
CREATE INDEX IF NOT EXISTS idx_history_provider_card ON provider_history(provider_id, card_id);
