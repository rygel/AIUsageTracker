use aic_core::config::ProviderManager;
use crate::database::{Database, HistoricalUsageRecord, ResetEvent};
use crate::config::AgentConfig;
use std::sync::Arc;
use std::time::Duration;
use tokio::sync::RwLock;
use tokio::time::interval;
use tracing::{info, error, debug};
use chrono::{DateTime, Utc};
use anyhow::Result;

pub struct Scheduler {
    provider_manager: Arc<ProviderManager>,
    db: Arc<Database>,
    config: Arc<RwLock<AgentConfig>>,
}

pub type SchedulerResult<T> = Result<T>;

impl Scheduler {
    pub async fn new(
        provider_manager: Arc<ProviderManager>,
        db: Arc<Database>,
        config: Arc<RwLock<AgentConfig>>,
    ) -> Result<Self> {
        Ok(Self {
            provider_manager,
            db,
            config,
        })
    }

    pub async fn run(&self) -> SchedulerResult<()> {
        let mut tick = interval(Duration::from_secs(60));

        loop {
            tick.tick().await;

            let config = self.config.read().await;

            if config.auto_refresh_enabled {
                debug!("Auto-refresh enabled, checking if refresh is due");

                let interval_secs = config.refresh_interval_minutes * 60;

                let last_refresh = self.db.get_latest_usage_records(1).await;
                let should_refresh = match last_refresh.first() {
                    Some(record) => {
                        match DateTime::parse_from_rfc3339(&record.timestamp) {
                            Ok(dt) => {
                                let elapsed = (Utc::now() - dt.with_timezone(&Utc)).num_seconds();
                                elapsed as u64 >= interval_secs
                            }
                            Err(_) => true,
                        }
                    }
                    None => true,
                };

                if should_refresh {
                    info!("Triggering scheduled refresh");
                    self.refresh_and_store().await?;
                }
            } else {
                debug!("Auto-refresh disabled");
            }
        }
    }

    async fn refresh_and_store(&self) -> SchedulerResult<()> {
        let usages = self.provider_manager.get_all_usage(true).await;
        let mut records = Vec::new();

        for u in usages.iter() {
            if !u.is_available {
                continue;
            }

            // Store main provider record with actual reset time from API
            let next_reset = u.next_reset_time.map(|dt| dt.to_rfc3339());
            
            records.push(HistoricalUsageRecord {
                id: uuid::Uuid::new_v4().to_string(),
                provider_id: u.provider_id.clone(),
                provider_name: u.provider_name.clone(),
                usage: u.cost_used,
                limit: if u.cost_limit > 0.0 { Some(u.cost_limit) } else { None },
                usage_unit: u.usage_unit.clone(),
                is_quota_based: u.is_quota_based,
                timestamp: Utc::now().to_rfc3339(),
                next_reset_time: next_reset.clone(),
            });

            // For Antigravity, store each model separately with its own reset time
            if u.provider_id == "antigravity" {
                if let Some(ref details) = u.details {
                    for detail in details {
                        let model_reset_time = detail.next_reset_time.map(|dt| dt.to_rfc3339());
                        
                        // Parse usage from detail (e.g., "65%" -> 65.0)
                        let model_usage = detail.used.parse::<f64>().unwrap_or(0.0);
                        
                        records.push(HistoricalUsageRecord {
                            id: uuid::Uuid::new_v4().to_string(),
                            provider_id: format!("{}-{}", u.provider_id, detail.name),
                            provider_name: format!("{} - {}", u.provider_name, detail.name),
                            usage: model_usage,
                            limit: Some(100.0), // All models are percentage-based
                            usage_unit: "%".to_string(),
                            is_quota_based: true,
                            timestamp: Utc::now().to_rfc3339(),
                            next_reset_time: model_reset_time,
                        });
                    }
                }
            }
        }

        info!("Collected {} provider records (including {} model-specific)", 
              records.len(),
              records.iter().filter(|r| r.provider_id.contains("-")).count()
        );

        for record in &records {
            if let Err(e) = self.db.insert_usage_record(record).await {
                error!("Failed to insert usage record for {}: {}", record.provider_id, e);
            }
        }

        if records.is_empty() {
            info!("No provider usage records to store");
        } else {
            info!("Successfully stored {} usage records", records.len());
        }

        Ok(())
    }
}
