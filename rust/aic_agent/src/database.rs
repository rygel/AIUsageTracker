use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use libsql::Builder;
use anyhow::Result;
use tracing::info;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct HistoricalUsageRecord {
    pub id: String,
    pub provider_id: String,
    pub provider_name: String,
    pub usage: f64,
    pub limit: Option<f64>,
    pub usage_unit: String,
    pub is_quota_based: bool,
    pub timestamp: String,
    pub next_reset_time: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ResetEvent {
    pub id: String,
    pub provider_id: String,
    pub provider_name: String,
    pub previous_usage: Option<f64>,
    pub new_usage: Option<f64>,
    pub reset_type: String,
    pub timestamp: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct RawResponse {
    pub id: String,
    pub provider_id: String,
    pub timestamp: i64,
    pub response_body: String,
}

pub struct Database {
    db: libsql::Database,
}

impl Database {
    pub async fn new(db_path: &std::path::Path) -> Result<Self> {
        let db = Builder::new_local(db_path.to_str().unwrap())
            .build()
            .await?;

        let db_instance = Self { db };

        db_instance.migrate().await?;

        Ok(db_instance)
    }

    async fn migrate(&self) -> Result<()> {
        let conn = self.db.connect()?;

        // 1. Create providers table
        conn.execute(
            r#"
            CREATE TABLE IF NOT EXISTS providers (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                unit TEXT NOT NULL,
                is_quota INTEGER NOT NULL
            )
            "#,
            (),
        ).await?;

        // 2. Create optimized usage history table
        conn.execute(
            r#"
            CREATE TABLE IF NOT EXISTS usage_history (
                provider_id TEXT NOT NULL,
                timestamp INTEGER NOT NULL,
                usage REAL NOT NULL,
                "limit" REAL,
                next_reset INTEGER,
                PRIMARY KEY (provider_id, timestamp),
                FOREIGN KEY(provider_id) REFERENCES providers(id)
            ) WITHOUT ROWID
            "#,
            (),
        ).await?;

        // 3. Create raw responses table (24h retention)
        conn.execute(
            r#"
            CREATE TABLE IF NOT EXISTS raw_responses (
                id TEXT PRIMARY KEY,
                provider_id TEXT NOT NULL,
                timestamp INTEGER NOT NULL,
                response_body TEXT NOT NULL
            )
            "#,
            (),
        ).await?;

        // 3. Create latest records cache for delta logic
        conn.execute(
            r#"
            CREATE TABLE IF NOT EXISTS latest_records (
                provider_id TEXT PRIMARY KEY,
                usage REAL NOT NULL,
                timestamp INTEGER NOT NULL,
                FOREIGN KEY(provider_id) REFERENCES providers(id)
            )
            "#,
            (),
        ).await?;

        // 4. Create reset events table (keeping it separate as it's infrequent)
        conn.execute(
            r#"
            CREATE TABLE IF NOT EXISTS reset_events (
                id TEXT PRIMARY KEY,
                provider_id TEXT NOT NULL,
                previous_usage REAL,
                new_usage REAL,
                reset_type TEXT NOT NULL,
                timestamp INTEGER NOT NULL
            )
            "#,
            (),
        ).await?;

        // 5. Check if legacy usage_records table exists and migrate if so
        let mut rows = conn.query("SELECT name FROM sqlite_master WHERE type='table' AND name='usage_records'", ()).await?;
        if rows.next().await?.is_some() {
            info!("Migrating legacy usage_records to new normalized schema...");
            
            // Migrate providers metadata
            conn.execute(
                r#"
                INSERT OR IGNORE INTO providers (id, name, unit, is_quota)
                SELECT DISTINCT provider_id, provider_name, usage_unit, is_quota_based
                FROM usage_records
                "#,
                (),
            ).await?;

            // Migrate usage records
            // We need to parse ISO 8601 strings to unix timestamps. 
            // SQLite's unixepoch function can help if the strings are formatted correctly.
            conn.execute(
                r#"
                INSERT OR IGNORE INTO usage_history (provider_id, timestamp, usage, "limit", next_reset)
                SELECT 
                    provider_id, 
                    unixepoch(timestamp), 
                    usage, 
                    "limit", 
                    CASE WHEN next_reset_time IS NOT NULL THEN unixepoch(next_reset_time) ELSE NULL END
                FROM usage_records
                "#,
                (),
            ).await?;

            // Populate latest_records cache
            conn.execute(
                r#"
                INSERT OR IGNORE INTO latest_records (provider_id, usage, timestamp)
                SELECT provider_id, usage, timestamp
                FROM (
                    SELECT provider_id, usage, unixepoch(timestamp) as timestamp,
                           ROW_NUMBER() OVER (PARTITION BY provider_id ORDER BY timestamp DESC) as rn
                    FROM usage_records
                ) WHERE rn = 1
                "#,
                (),
            ).await?;

            // Rename legacy table instead of deleting to be safe
            conn.execute("ALTER TABLE usage_records RENAME TO usage_records_legacy", ()).await?;
            info!("Migration complete. Legacy table renamed to usage_records_legacy.");
        }

        Ok(())
    }

    pub async fn insert_usage_record(&self, record: &HistoricalUsageRecord) -> Result<()> {
        let conn = self.db.connect()?;

        // 1. Ensure provider exists
        conn.execute(
            "INSERT OR REPLACE INTO providers (id, name, unit, is_quota) VALUES (?1, ?2, ?3, ?4)",
            (
                record.provider_id.as_str(),
                record.provider_name.as_str(),
                record.usage_unit.as_str(),
                if record.is_quota_based { 1 } else { 0 },
            ),
        ).await?;

        // 2. Parse timestamp
        let ts = DateTime::parse_from_rfc3339(&record.timestamp)?
            .with_timezone(&Utc)
            .timestamp();
        
        let next_reset = record.next_reset_time.as_deref().and_then(|t| {
            DateTime::parse_from_rfc3339(t).ok().map(|dt| dt.timestamp())
        });

        // 3. Insert into history
        conn.execute(
            r#"
            INSERT OR REPLACE INTO usage_history (provider_id, timestamp, usage, "limit", next_reset)
            VALUES (?1, ?2, ?3, ?4, ?5)
            "#,
            (
                record.provider_id.as_str(),
                ts,
                record.usage,
                record.limit,
                next_reset,
            ),
        ).await?;

        // 4. Update latest cache
        conn.execute(
            r#"
            INSERT OR REPLACE INTO latest_records (provider_id, usage, timestamp)
            VALUES (?1, ?2, ?3)
            "#,
            (
                record.provider_id.as_str(),
                record.usage,
                ts,
            ),
        ).await?;

        Ok(())
    }

    pub async fn get_all_usage_records(&self) -> Vec<HistoricalUsageRecord> {
        let conn = match self.db.connect() {
            Ok(c) => c,
            Err(_) => return Vec::new(),
        };
        
        let mut rows = match conn.query(
            r#"
            SELECT h.provider_id, p.name, h.usage, h."limit", p.unit, p.is_quota, h.timestamp, h.next_reset
            FROM usage_history h
            JOIN providers p ON h.provider_id = p.id
            ORDER BY h.timestamp DESC
            "#,
            (),
        ).await {
            Ok(r) => r,
            Err(_) => return Vec::new(),
        };

        let mut records = Vec::new();
        while let Some(row) = rows.next().await.ok().flatten() {
            if let Ok(record) = row_to_historical_usage(&row) {
                records.push(record);
            }
        }
        
        records
    }

    pub async fn get_usage_records_by_provider(&self, provider_id: &str) -> Vec<HistoricalUsageRecord> {
        let conn = match self.db.connect() {
            Ok(c) => c,
            Err(_) => return Vec::new(),
        };
        
        let mut rows = match conn.query(
            r#"
            SELECT h.provider_id, p.name, h.usage, h."limit", p.unit, p.is_quota, h.timestamp, h.next_reset
            FROM usage_history h
            JOIN providers p ON h.provider_id = p.id
            WHERE h.provider_id = ?1
            ORDER BY h.timestamp DESC
            "#,
            [provider_id],
        ).await {
            Ok(r) => r,
            Err(_) => return Vec::new(),
        };

        let mut records = Vec::new();
        while let Some(row) = rows.next().await.ok().flatten() {
            if let Ok(record) = row_to_historical_usage(&row) {
                records.push(record);
            }
        }
        
        records
    }

    pub async fn get_usage_records_by_time_range(
        &self,
        start: DateTime<Utc>,
        end: DateTime<Utc>,
    ) -> Vec<HistoricalUsageRecord> {
        let conn = match self.db.connect() {
            Ok(c) => c,
            Err(_) => return Vec::new(),
        };
        
        let mut rows = match conn.query(
            r#"
            SELECT h.provider_id, p.name, h.usage, h."limit", p.unit, p.is_quota, h.timestamp, h.next_reset
            FROM usage_history h
            JOIN providers p ON h.provider_id = p.id
            WHERE h.timestamp >= ?1 AND h.timestamp <= ?2
            ORDER BY h.timestamp DESC
            "#,
            (start.timestamp(), end.timestamp()),
        ).await {
            Ok(r) => r,
            Err(_) => return Vec::new(),
        };

        let mut records = Vec::new();
        while let Some(row) = rows.next().await.ok().flatten() {
            if let Ok(record) = row_to_historical_usage(&row) {
                records.push(record);
            }
        }
        
        records
    }

    pub async fn get_latest_usage_records(&self, limit: usize) -> Vec<HistoricalUsageRecord> {
        let conn = match self.db.connect() {
            Ok(c) => c,
            Err(_) => return Vec::new(),
        };
        
        let mut rows = match conn.query(
            r#"
            SELECT h.provider_id, p.name, h.usage, h."limit", p.unit, p.is_quota, h.timestamp, h.next_reset
            FROM usage_history h
            JOIN providers p ON h.provider_id = p.id
            ORDER BY h.timestamp DESC
            LIMIT ?1
            "#,
            [limit as i64],
        ).await {
            Ok(r) => r,
            Err(_) => return Vec::new(),
        };

        let mut records = Vec::new();
        while let Some(row) = rows.next().await.ok().flatten() {
            if let Ok(record) = row_to_historical_usage(&row) {
                records.push(record);
            }
        }
        
        records
    }

    pub async fn cleanup_old_records(&self, days: i64) -> Result<u64> {
        let cutoff = (Utc::now() - chrono::Duration::days(days)).timestamp();
        let conn = self.db.connect()?;

        let result = conn.execute(
            r#"
            DELETE FROM usage_history
            WHERE timestamp < ?1
            "#,
            [cutoff],
        ).await?;

        Ok(result)
    }

    pub async fn insert_reset_event(&self, event: &ResetEvent) -> Result<()> {
        let conn = self.db.connect()?;

        let ts = DateTime::parse_from_rfc3339(&event.timestamp)?
            .with_timezone(&Utc)
            .timestamp();

        conn.execute(
            r#"
            INSERT OR REPLACE INTO reset_events 
            (id, provider_id, previous_usage, new_usage, reset_type, timestamp)
            VALUES (?1, ?2, ?3, ?4, ?5, ?6)
            "#,
            (
                event.id.as_str(),
                event.provider_id.as_str(),
                event.previous_usage,
                event.new_usage,
                event.reset_type.as_str(),
                ts,
            ),
        ).await?;

        Ok(())
    }

    pub async fn get_reset_events(&self, provider_id: Option<&str>) -> Vec<ResetEvent> {
        let conn = match self.db.connect() {
            Ok(c) => c,
            Err(_) => return Vec::new(),
        };

        let mut rows = if let Some(pid) = provider_id {
            match conn.query(
                r#"
                SELECT r.id, r.provider_id, p.name, r.previous_usage, r.new_usage, r.reset_type, r.timestamp
                FROM reset_events r
                JOIN providers p ON r.provider_id = p.id
                WHERE r.provider_id = ?1
                ORDER BY r.timestamp DESC
                "#,
                [pid],
            ).await {
                Ok(r) => r,
                Err(_) => return Vec::new(),
            }
        } else {
            match conn.query(
                r#"
                SELECT r.id, r.provider_id, p.name, r.previous_usage, r.new_usage, r.reset_type, r.timestamp
                FROM reset_events r
                JOIN providers p ON r.provider_id = p.id
                ORDER BY r.timestamp DESC
                "#,
                (),
            ).await {
                Ok(r) => r,
                Err(_) => return Vec::new(),
            }
        };

        let mut events = Vec::new();
        while let Some(row) = rows.next().await.ok().flatten() {
            if let Ok(event) = row_to_reset_event(&row) {
                events.push(event);
            }
        }

        events
    }

    pub async fn get_reset_events_by_time_range(
        &self,
        provider_id: Option<&str>,
        start: DateTime<Utc>,
        end: DateTime<Utc>,
    ) -> Vec<ResetEvent> {
        let conn = match self.db.connect() {
            Ok(c) => c,
            Err(_) => return Vec::new(),
        };

        let mut rows = if let Some(pid) = provider_id {
            match conn.query(
                r#"
                SELECT r.id, r.provider_id, p.name, r.previous_usage, r.new_usage, r.reset_type, r.timestamp
                FROM reset_events r
                JOIN providers p ON r.provider_id = p.id
                WHERE r.provider_id = ?1 AND r.timestamp >= ?2 AND r.timestamp <= ?3
                ORDER BY r.timestamp DESC
                "#,
                [pid.to_string(), start.timestamp().to_string(), end.timestamp().to_string()],
            ).await {
                Ok(r) => r,
                Err(_) => return Vec::new(),
            }
        } else {
            match conn.query(
                r#"
                SELECT r.id, r.provider_id, p.name, r.previous_usage, r.new_usage, r.reset_type, r.timestamp
                FROM reset_events r
                JOIN providers p ON r.provider_id = p.id
                WHERE r.timestamp >= ?1 AND r.timestamp <= ?2
                ORDER BY r.timestamp DESC
                "#,
                (start.timestamp(), end.timestamp()),
            ).await {
                Ok(r) => r,
                Err(_) => return Vec::new(),
            }
        };

        let mut events = Vec::new();
        while let Some(row) = rows.next().await.ok().flatten() {
            if let Ok(event) = row_to_reset_event(&row) {
                events.push(event);
            }
        }

        events
    }

    pub async fn insert_raw_response(&self, provider_id: &str, body: &str) -> Result<()> {
        let conn = self.db.connect()?;
        let id = format!("{}-{}", provider_id, Utc::now().timestamp());
        let ts = Utc::now().timestamp();

        conn.execute(
            "INSERT OR REPLACE INTO raw_responses (id, provider_id, timestamp, response_body) VALUES (?1, ?2, ?3, ?4)",
            (id, provider_id, ts, body),
        ).await?;

        Ok(())
    }

    pub async fn get_raw_responses(&self, provider_id: Option<String>, limit: usize) -> Vec<RawResponse> {
        let conn = match self.db.connect() {
            Ok(c) => c,
            Err(_) => return Vec::new(),
        };

        let mut rows = if let Some(pid) = provider_id {
            let sql = "SELECT id, provider_id, timestamp, response_body FROM raw_responses WHERE provider_id = ?1 ORDER BY timestamp DESC LIMIT ?2";
            match conn.query(sql, (pid, limit as i64)).await {
                Ok(r) => r,
                Err(_) => return Vec::new(),
            }
        } else {
            let sql = "SELECT id, provider_id, timestamp, response_body FROM raw_responses ORDER BY timestamp DESC LIMIT ?1";
            match conn.query(sql, [limit as i64]).await {
                Ok(r) => r,
                Err(_) => return Vec::new(),
            }
        };

        let mut logs = Vec::new();
        while let Ok(Some(row)) = rows.next().await {
            if let (Ok(id), Ok(pid), Ok(ts), Ok(body)) = (
                row.get::<String>(0),
                row.get::<String>(1),
                row.get::<i64>(2),
                row.get::<String>(3),
            ) {
                logs.push(RawResponse {
                    id,
                    provider_id: pid,
                    timestamp: ts,
                    response_body: body,
                });
            }
        }
        logs
    }


    pub async fn cleanup_raw_responses(&self) -> Result<()> {
        let conn = self.db.connect()?;
        let twenty_four_hours_ago = Utc::now().timestamp() - (24 * 60 * 60);

        conn.execute(
            "DELETE FROM raw_responses WHERE timestamp < ?1",
            [twenty_four_hours_ago],
        ).await?;

        Ok(())
    }

    pub async fn get_latest_usage_for_provider(&self, provider_id: &str) -> Option<HistoricalUsageRecord> {
        let conn = match self.db.connect() {
            Ok(c) => c,
            Err(_) => return None,
        };

        let mut rows = match conn.query(
            r#"
            SELECT h.provider_id, p.name, h.usage, h."limit", p.unit, p.is_quota, h.timestamp, h.next_reset
            FROM latest_records h
            JOIN providers p ON h.provider_id = p.id
            WHERE h.provider_id = ?1
            LIMIT 1
            "#,
            [provider_id],
        ).await {
            Ok(r) => r,
            Err(_) => return None,
        };

        if let Some(row) = rows.next().await.ok().flatten() {
            if let Ok(record) = row_to_historical_usage(&row) {
                return Some(record);
            }
        }

        None
    }
}

fn row_to_historical_usage(row: &libsql::Row) -> Result<HistoricalUsageRecord> {
    let ts_int: i64 = row.get(6)?;
    let ts_str = DateTime::from_timestamp(ts_int, 0)
        .unwrap_or_else(|| Utc::now())
        .to_rfc3339();
    
    let next_reset_int: Option<i64> = row.get(7).ok();
    let next_reset_str = next_reset_int.map(|ts| {
        DateTime::from_timestamp(ts, 0)
            .unwrap_or_else(|| Utc::now())
            .to_rfc3339()
    });

    Ok(HistoricalUsageRecord {
        id: format!("{}-{}", row.get::<String>(0)?, ts_int), // Synthetic ID since we removed UUIDs
        provider_id: row.get(0)?,
        provider_name: row.get(1)?,
        usage: row.get(2)?,
        limit: row.get(3)?,
        usage_unit: row.get(4)?,
        is_quota_based: row.get::<i64>(5)? == 1,
        timestamp: ts_str,
        next_reset_time: next_reset_str,
    })
}

fn row_to_reset_event(row: &libsql::Row) -> Result<ResetEvent> {
    let ts_int: i64 = row.get(6)?;
    let ts_str = DateTime::from_timestamp(ts_int, 0)
        .unwrap_or_else(|| Utc::now())
        .to_rfc3339();

    Ok(ResetEvent {
        id: row.get(0)?,
        provider_id: row.get(1)?,
        provider_name: row.get(2)?,
        previous_usage: row.get(3).ok(),
        new_usage: row.get(4).ok(),
        reset_type: row.get(5)?,
        timestamp: ts_str,
    })
}

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::TempDir;

    async fn create_test_db() -> (Database, TempDir) {
        let temp_dir = TempDir::new().unwrap();
        let db_path = temp_dir.path().join("test.db");
        let db = Database::new(&db_path).await.unwrap();
        (db, temp_dir)
    }

    fn create_test_record(id: &str, provider_id: &str, provider_name: &str, usage: f64, timestamp: &str) -> HistoricalUsageRecord {
        HistoricalUsageRecord {
            id: id.to_string(),
            provider_id: provider_id.to_string(),
            provider_name: provider_name.to_string(),
            usage,
            limit: Some(100.0),
            usage_unit: "credits".to_string(),
            is_quota_based: true,
            timestamp: timestamp.to_string(),
            next_reset_time: None,
        }
    }

    #[tokio::test]
    async fn test_database_creation_and_migration() {
        let temp_dir = TempDir::new().unwrap();
        let db_path = temp_dir.path().join("new_test.db");
        
        let db = Database::new(&db_path).await;
        assert!(db.is_ok());
    }

    #[tokio::test]
    async fn test_insert_and_get_all_usage_records() {
        let (db, _temp_dir) = create_test_db().await;
        
        let record = create_test_record(
            "test-1",
            "openai",
            "OpenAI",
            50.0,
            "2026-02-09T10:00:00Z"
        );
        
        let result = db.insert_usage_record(&record).await;
        assert!(result.is_ok());
        
        let records = db.get_all_usage_records().await;
        assert_eq!(records.len(), 1);
        assert!(records[0].id.starts_with("openai-"));
        assert_eq!(records[0].provider_id, "openai");
        assert_eq!(records[0].usage, 50.0);
    }

    #[tokio::test]
    async fn test_insert_multiple_records() {
        let (db, _temp_dir) = create_test_db().await;
        
        let record1 = create_test_record(
            "test-1",
            "openai",
            "OpenAI",
            50.0,
            "2026-02-09T10:00:00Z"
        );
        let record2 = create_test_record(
            "test-2",
            "anthropic",
            "Anthropic",
            75.0,
            "2026-02-09T11:00:00Z"
        );
        
        db.insert_usage_record(&record1).await.unwrap();
        db.insert_usage_record(&record2).await.unwrap();
        
        let records = db.get_all_usage_records().await;
        assert_eq!(records.len(), 2);
    }

    #[tokio::test]
    async fn test_get_usage_records_by_provider() {
        let (db, _temp_dir) = create_test_db().await;
        
        let openai_record = create_test_record(
            "test-1",
            "openai",
            "OpenAI",
            50.0,
            "2026-02-09T10:00:00Z"
        );
        let anthropic_record = create_test_record(
            "test-2",
            "anthropic",
            "Anthropic",
            75.0,
            "2026-02-09T11:00:00Z"
        );
        
        db.insert_usage_record(&openai_record).await.unwrap();
        db.insert_usage_record(&anthropic_record).await.unwrap();
        
        let openai_records = db.get_usage_records_by_provider("openai").await;
        assert_eq!(openai_records.len(), 1);
        assert_eq!(openai_records[0].provider_id, "openai");
        assert_eq!(openai_records[0].provider_name, "OpenAI");
        
        let anthropic_records = db.get_usage_records_by_provider("anthropic").await;
        assert_eq!(anthropic_records.len(), 1);
        assert_eq!(anthropic_records[0].provider_id, "anthropic");
    }

    #[tokio::test]
    async fn test_get_usage_records_by_time_range() {
        let (db, _temp_dir) = create_test_db().await;
        
        let record1 = create_test_record(
            "test-1",
            "openai",
            "OpenAI",
            50.0,
            "2026-02-09T10:00:00Z"
        );
        let record2 = create_test_record(
            "test-2",
            "openai",
            "OpenAI",
            75.0,
            "2026-02-09T15:00:00Z"
        );
        let record3 = create_test_record(
            "test-3",
            "openai",
            "OpenAI",
            100.0,
            "2026-02-10T10:00:00Z"
        );
        
        db.insert_usage_record(&record1).await.unwrap();
        db.insert_usage_record(&record2).await.unwrap();
        db.insert_usage_record(&record3).await.unwrap();
        
        let start = DateTime::parse_from_rfc3339("2026-02-09T12:00:00Z").unwrap().with_timezone(&Utc);
        let end = DateTime::parse_from_rfc3339("2026-02-10T00:00:00Z").unwrap().with_timezone(&Utc);
        
        let records = db.get_usage_records_by_time_range(start, end).await;
        assert_eq!(records.len(), 1);
        assert!(records[0].id.starts_with("openai-"));
        assert_eq!(records[0].usage, 75.0);
    }

    #[tokio::test]
    async fn test_get_latest_usage_records_with_limit() {
        let (db, _temp_dir) = create_test_db().await;
        
        for i in 0..5 {
            let record = create_test_record(
                &format!("test-{}", i),
                "openai",
                "OpenAI",
                i as f64 * 10.0,
                &format!("2026-02-09T{:02}:00:00Z", i)
            );
            db.insert_usage_record(&record).await.unwrap();
        }
        
        let records = db.get_latest_usage_records(3).await;
        assert_eq!(records.len(), 3);
        assert!(records[0].id.starts_with("openai-"));
        assert_eq!(records[0].usage, 40.0);
        assert_eq!(records[1].usage, 30.0);
        assert_eq!(records[2].usage, 20.0);
    }

    #[tokio::test]
    async fn test_upsert_existing_record() {
        let (db, _temp_dir) = create_test_db().await;
        
        let record = create_test_record(
            "test-1",
            "openai",
            "OpenAI",
            50.0,
            "2026-02-09T10:00:00Z"
        );
        
        db.insert_usage_record(&record).await.unwrap();
        
        // Use SAME timestamp to test upsert (replace) logic on (provider_id, timestamp)
        let updated_record = HistoricalUsageRecord {
            id: "test-1".to_string(),
            provider_id: "openai".to_string(),
            provider_name: "OpenAI Updated".to_string(),
            usage: 100.0,
            limit: Some(200.0),
            usage_unit: "tokens".to_string(),
            is_quota_based: false,
            timestamp: "2026-02-09T10:00:00Z".to_string(),
            next_reset_time: None,
        };
        
        db.insert_usage_record(&updated_record).await.unwrap();
        
        let records = db.get_all_usage_records().await;
        assert_eq!(records.len(), 1);
        assert_eq!(records[0].usage, 100.0);
        assert_eq!(records[0].provider_name, "OpenAI Updated");
        assert_eq!(records[0].usage_unit, "tokens");
        assert!(!records[0].is_quota_based);
    }

    #[tokio::test]
    async fn test_cleanup_old_records() {
        let (db, _temp_dir) = create_test_db().await;
        
        let old_record = create_test_record(
            "old-1",
            "openai",
            "OpenAI",
            50.0,
            "2026-01-01T00:00:00Z"
        );
        let recent_record = create_test_record(
            "recent-1",
            "openai",
            "OpenAI",
            100.0,
            "2026-02-09T10:00:00Z"
        );
        
        db.insert_usage_record(&old_record).await.unwrap();
        db.insert_usage_record(&recent_record).await.unwrap();
        
        let deleted_count = db.cleanup_old_records(30).await.unwrap();
        assert_eq!(deleted_count, 1);
        
        let records = db.get_all_usage_records().await;
        assert_eq!(records.len(), 1);
        assert!(records[0].id.starts_with("openai-"));
    }

    #[tokio::test]
    async fn test_get_nonexistent_provider_returns_empty() {
        let (db, _temp_dir) = create_test_db().await;
        
        let records = db.get_usage_records_by_provider("nonexistent").await;
        assert!(records.is_empty());
    }

    #[tokio::test]
    async fn test_record_ordering_by_timestamp_desc() {
        let (db, _temp_dir) = create_test_db().await;
        
        let record1 = create_test_record(
            "test-1",
            "openai",
            "OpenAI",
            50.0,
            "2026-02-09T10:00:00Z"
        );
        let record2 = create_test_record(
            "test-2",
            "openai",
            "OpenAI",
            75.0,
            "2026-02-09T12:00:00Z"
        );
        let record3 = create_test_record(
            "test-3",
            "openai",
            "OpenAI",
            100.0,
            "2026-02-09T08:00:00Z"
        );
        
        db.insert_usage_record(&record1).await.unwrap();
        db.insert_usage_record(&record2).await.unwrap();
        db.insert_usage_record(&record3).await.unwrap();
        
        let records = db.get_all_usage_records().await;
        // test-2 is most recent (12:00)
        assert!(records[0].id.starts_with("openai-"));
        assert_eq!(records[0].usage, 75.0);
        assert_eq!(records[1].usage, 50.0);
        assert_eq!(records[2].usage, 100.0);
    }

    #[tokio::test]
    async fn test_null_limit_handling() {
        let (db, _temp_dir) = create_test_db().await;
        
        let record = HistoricalUsageRecord {
            id: "test-1".to_string(),
            provider_id: "openai".to_string(),
            provider_name: "OpenAI".to_string(),
            usage: 50.0,
            limit: None,
            usage_unit: "credits".to_string(),
            is_quota_based: false,
            timestamp: "2026-02-09T10:00:00Z".to_string(),
            next_reset_time: None,
        };
        
        db.insert_usage_record(&record).await.unwrap();
        
        let records = db.get_all_usage_records().await;
        assert_eq!(records.len(), 1);
        assert!(records[0].limit.is_none());
    }

    #[tokio::test]
    async fn test_migration_from_legacy() {
        let temp_dir = tempfile::tempdir().unwrap();
        let db_path = temp_dir.path().join("migration_test.db");
        
        // 1. Create legacy table manually
        let db = Builder::new_local(db_path.to_str().unwrap()).build().await.unwrap();
        let conn = db.connect().unwrap();
        conn.execute_batch(r#"
            CREATE TABLE usage_records (
                id TEXT PRIMARY KEY,
                provider_id TEXT NOT NULL,
                provider_name TEXT NOT NULL,
                usage REAL NOT NULL,
                "limit" REAL,
                usage_unit TEXT NOT NULL,
                is_quota_based INTEGER NOT NULL,
                timestamp TEXT NOT NULL,
                next_reset_time TEXT
            );
            INSERT INTO usage_records VALUES (
                'old-uuid', 'openai', 'OpenAI', 42.0, 100.0, 'tokens', 0, '2026-02-13T10:00:00Z', NULL
            );
        "#).await.unwrap();

        // 2. Initialize Database wrapper (triggers migration)
        let wrapped_db = Database::new(&temp_dir.path().join("migration_test.db")).await.unwrap();

        // 3. Verify data moved
        let records = wrapped_db.get_all_usage_records().await;
        assert_eq!(records.len(), 1);
        assert_eq!(records[0].provider_id, "openai");
        assert_eq!(records[0].usage, 42.0);
        
        // 4. Verify legacy table was renamed
        let mut rows = conn.query("SELECT name FROM sqlite_master WHERE type='table' AND name='usage_records_legacy'", ()).await.unwrap();
        assert!(rows.next().await.unwrap().is_some());
    }
}
