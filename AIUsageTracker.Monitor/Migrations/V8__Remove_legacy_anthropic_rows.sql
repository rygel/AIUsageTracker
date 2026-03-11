-- Remove deprecated Anthropic provider data.
-- Anthropic is no longer a supported standalone provider id.

DELETE FROM provider_history
WHERE lower(provider_id) = 'anthropic';

DELETE FROM raw_snapshots
WHERE lower(provider_id) = 'anthropic';

DELETE FROM reset_events
WHERE lower(provider_id) = 'anthropic';

DELETE FROM providers
WHERE lower(provider_id) = 'anthropic';
