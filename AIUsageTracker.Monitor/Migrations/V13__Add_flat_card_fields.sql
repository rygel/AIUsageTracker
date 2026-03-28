-- Add flat-card fields to provider_history.
-- window_kind: integer enum (WindowKind) — None=0, Burst=1, Rolling=2, ModelSpecific=3.
-- model_name:  the model identifier for model-specific cards (e.g. "gemini-2.5-flash").
-- name:        human-readable display label for the card (e.g. "5-hour quota").
ALTER TABLE provider_history ADD COLUMN window_kind INTEGER NOT NULL DEFAULT 0;
ALTER TABLE provider_history ADD COLUMN model_name TEXT;
ALTER TABLE provider_history ADD COLUMN name TEXT;
