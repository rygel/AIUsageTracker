-- Add upstream response validity metadata to provider_history for stable API diagnostics

ALTER TABLE provider_history ADD COLUMN http_status INTEGER NOT NULL DEFAULT 0;
ALTER TABLE provider_history ADD COLUMN upstream_response_validity INTEGER NOT NULL DEFAULT 0;
ALTER TABLE provider_history ADD COLUMN upstream_response_note TEXT NOT NULL DEFAULT '';
