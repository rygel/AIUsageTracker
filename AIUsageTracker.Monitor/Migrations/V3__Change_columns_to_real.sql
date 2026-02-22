-- Change provider_history numeric columns from INTEGER to REAL
-- to support fractional values (e.g., $19.90)

ALTER TABLE provider_history RENAME COLUMN requests_used TO requests_used_old;
ALTER TABLE provider_history RENAME COLUMN requests_available TO requests_available_old;
ALTER TABLE provider_history RENAME COLUMN requests_percentage TO requests_percentage_old;

ALTER TABLE provider_history ADD COLUMN requests_used REAL NOT NULL DEFAULT 0;
ALTER TABLE provider_history ADD COLUMN requests_available REAL NOT NULL DEFAULT 0;
ALTER TABLE provider_history ADD COLUMN requests_percentage REAL NOT NULL DEFAULT 0;

UPDATE provider_history SET
    requests_used = CAST(requests_used_old AS REAL),
    requests_available = CAST(requests_available_old AS REAL),
    requests_percentage = CAST(requests_percentage_old AS REAL);

ALTER TABLE provider_history DROP COLUMN requests_used_old;
ALTER TABLE provider_history DROP COLUMN requests_available_old;
ALTER TABLE provider_history DROP COLUMN requests_percentage_old;
