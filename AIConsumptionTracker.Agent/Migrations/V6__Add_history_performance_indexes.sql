-- Add targeted indexes for large history datasets

CREATE INDEX IF NOT EXISTS idx_history_fetched_time
ON provider_history(fetched_at DESC);

CREATE INDEX IF NOT EXISTS idx_history_provider_id_desc
ON provider_history(provider_id, id DESC);
