-- Add card_type to provider_history for polymorphic ProviderUsage subtype tracking.
-- Values: "quota", "windowed", "model", "status"
-- Null indicates legacy row — treated as "quota" on read.
ALTER TABLE provider_history ADD COLUMN card_type TEXT;
