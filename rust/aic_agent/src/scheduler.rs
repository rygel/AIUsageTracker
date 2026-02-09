use aic_core::config::ProviderManager;
use crate::database::{Database, HistoricalUsageRecord};
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

        let records: Vec<HistoricalUsageRecord> = usages
            .iter()
            .filter_map(|u| {
                if u.is_available && u.cost_used > 0.0 {
                    Some(HistoricalUsageRecord {
                        id: uuid::Uuid::new_v4().to_string(),
                        provider_id: u.provider_id.clone(),
                        provider_name: u.provider_name.clone(),
                        usage: u.cost_used,
                        limit: if u.cost_limit > 0.0 { Some(u.cost_limit) } else { None },
                        usage_unit: u.usage_unit.clone(),
                        is_quota_based: u.is_quota_based,
                        timestamp: Utc::now().to_rfc3339(),
                    })
                } else {
                    None
                }
            })
            .collect();

        info!("Collected {} provider records", records.len());

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
