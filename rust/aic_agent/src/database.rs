use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use libsql::Builder;
use anyhow::Result;

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

        conn.execute(
            r#"
            CREATE TABLE IF NOT EXISTS usage_records (
                id TEXT PRIMARY KEY,
                provider_id TEXT NOT NULL,
                provider_name TEXT NOT NULL,
                usage REAL NOT NULL,
                "limit" REAL,
                usage_unit TEXT NOT NULL,
                is_quota_based INTEGER NOT NULL,
                timestamp TEXT NOT NULL
            )
            "#,
            (),
        ).await?;

        conn.execute(
            r#"
            CREATE INDEX IF NOT EXISTS idx_provider_id ON usage_records(provider_id);
            "#,
            (),
        ).await?;

        conn.execute(
            r#"
            CREATE INDEX IF NOT EXISTS idx_timestamp ON usage_records(timestamp);
            "#,
            (),
        ).await?;

        conn.execute(
            r#"
            CREATE INDEX IF NOT EXISTS idx_provider_timestamp ON usage_records(provider_id, timestamp);
            "#,
            (),
        ).await?;

        Ok(())
    }

    pub async fn insert_usage_record(&self, record: &HistoricalUsageRecord) -> Result<()> {
        let conn = self.db.connect()?;

        conn.execute(
            r#"
            INSERT OR REPLACE INTO usage_records (id, provider_id, provider_name, usage, "limit", usage_unit, is_quota_based, timestamp)
            VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8)
            "#,
            (
                record.id.as_str(),
                record.provider_id.as_str(),
                record.provider_name.as_str(),
                record.usage,
                record.limit,
                record.usage_unit.as_str(),
                if record.is_quota_based { 1 } else { 0 },
                record.timestamp.as_str(),
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
            SELECT id, provider_id, provider_name, usage, "limit", usage_unit, is_quota_based, timestamp
            FROM usage_records
            ORDER BY timestamp DESC
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
            SELECT id, provider_id, provider_name, usage, "limit", usage_unit, is_quota_based, timestamp
            FROM usage_records
            WHERE provider_id = ?1
            ORDER BY timestamp DESC
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
            SELECT id, provider_id, provider_name, usage, "limit", usage_unit, is_quota_based, timestamp
            FROM usage_records
            WHERE timestamp >= ?1 AND timestamp <= ?2
            ORDER BY timestamp DESC
            "#,
            (start.to_rfc3339(), end.to_rfc3339()),
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
            SELECT id, provider_id, provider_name, usage, "limit", usage_unit, is_quota_based, timestamp
            FROM usage_records
            ORDER BY timestamp DESC
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
        let cutoff = Utc::now() - chrono::Duration::days(days);
        let conn = self.db.connect()?;

        let result = conn.execute(
            r#"
            DELETE FROM usage_records
            WHERE timestamp < ?1
            "#,
            [cutoff.to_rfc3339()],
        ).await?;

        Ok(result)
    }
}

fn row_to_historical_usage(row: &libsql::Row) -> Result<HistoricalUsageRecord> {
    Ok(HistoricalUsageRecord {
        id: row.get(0)?,
        provider_id: row.get(1)?,
        provider_name: row.get(2)?,
        usage: row.get(3)?,
        limit: row.get(4)?,
        usage_unit: row.get(5)?,
        is_quota_based: row.get::<i64>(6)? == 1,
        timestamp: row.get(7)?,
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
        assert_eq!(records[0].id, "test-1");
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
        assert_eq!(records[0].id, "test-2");
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
        assert_eq!(records[0].id, "test-4");
        assert_eq!(records[1].id, "test-3");
        assert_eq!(records[2].id, "test-2");
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
        
        let updated_record = HistoricalUsageRecord {
            id: "test-1".to_string(),
            provider_id: "openai".to_string(),
            provider_name: "OpenAI Updated".to_string(),
            usage: 100.0,
            limit: Some(200.0),
            usage_unit: "tokens".to_string(),
            is_quota_based: false,
            timestamp: "2026-02-09T12:00:00Z".to_string(),
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
        assert_eq!(records[0].id, "recent-1");
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
        assert_eq!(records[0].id, "test-2");
        assert_eq!(records[1].id, "test-1");
        assert_eq!(records[2].id, "test-3");
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
        };
        
        db.insert_usage_record(&record).await.unwrap();
        
        let records = db.get_all_usage_records().await;
        assert_eq!(records.len(), 1);
        assert!(records[0].limit.is_none());
    }
}
