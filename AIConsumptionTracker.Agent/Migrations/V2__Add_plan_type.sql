-- Add plan_type and auth_source columns to providers table

ALTER TABLE providers ADD COLUMN plan_type TEXT DEFAULT 'usage';
ALTER TABLE providers ADD COLUMN auth_source TEXT DEFAULT 'manual';
