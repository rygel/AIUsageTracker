-- Add details_json column to provider_history to store sub-provider details

ALTER TABLE provider_history ADD COLUMN details_json TEXT DEFAULT NULL;
