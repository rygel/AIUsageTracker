mod agent;
mod http_client;
mod icons;
mod models;
mod tray;

use std::collections::HashSet;
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use eframe::egui;
use serde::{Deserialize, Serialize};
use tokio::sync::Mutex as TokioMutex;

use agent::AgentManager;
use http_client::{AgentClient, AgentStatus, GitHubAuthStatus, DeviceFlowResponse};
use models::{AgentInfo, AppPreferences, ProviderConfig, ProviderUsage};
use tray::{TrayManager, TrayEvent};

const REFRESH_INTERVAL_SECS: u64 = 30;
const POLL_INTERVAL_SECS: u64 = 2;  // Poll for incremental updates every 2 seconds

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AppConfig {
    pub show_all: bool,
    pub privacy_mode: bool,
    pub always_on_top: bool,
    pub compact_mode: bool,
    pub auto_refresh: bool,
    pub auto_start_agent: bool,
    pub color_threshold_yellow: i32,
    pub color_threshold_red: i32,
}

impl Default for AppConfig {
    fn default() -> Self {
        Self {
            show_all: false,
            privacy_mode: false,
            always_on_top: true,
            compact_mode: true,
            auto_refresh: true,
            auto_start_agent: true,
            color_threshold_yellow: 60,
            color_threshold_red: 80,
        }
    }
}

#[derive(Debug, Clone)]
pub struct LoadResult {
    pub providers: Vec<ProviderUsage>,
    pub agent_info: Option<AgentInfo>,
    pub agent_status: AgentStatus,
    pub error: Option<String>,
}

#[derive(Clone)]
pub enum BackgroundResult {
    Providers(Vec<serde_json::Value>),
    History(Vec<serde_json::Value>),
    GithubStatus(GitHubAuthStatus),
}

use icons::ProviderIcons;

pub struct AICApp {
    agent_client: AgentClient,
    agent_manager: Arc<TokioMutex<AgentManager>>,
    providers: Vec<ProviderUsage>,
    config: AppConfig,
    prefs: AppPreferences,
    agent_info: Option<AgentInfo>,
    agent_status: AgentStatus,
    last_refresh: Option<Instant>,
    error_message: Option<String>,
    settings_open: bool,
    settings_viewport_id: egui::ViewportId,
    selected_tab: usize,
    load_result: Arc<Mutex<Option<LoadResult>>>,
    background_result: Arc<Mutex<Option<BackgroundResult>>>,
    is_refreshing: bool,
    is_starting_agent: bool,
    runtime: tokio::runtime::Runtime,
    debug_log: Vec<String>,
    tray_manager: TrayManager,
    tray_receiver: Option<std::sync::mpsc::Receiver<TrayEvent>>,
    minimized_to_tray: bool,
    github_auth_status: Option<GitHubAuthStatus>,
    github_flow_state: Option<DeviceFlowResponse>,
    github_polling: bool,
    discovered_providers: Vec<serde_json::Value>,
    history: Vec<serde_json::Value>,
    raw_responses: Vec<serde_json::Value>,
    loading_providers: bool,
    loading_history: bool,
    loading_github_status: bool,
    expanded_sub_providers: HashSet<String>,
    expanded_groups: HashSet<String>,
    provider_icons: ProviderIcons,
    last_poll: Option<Instant>,
}

impl Default for AICApp {
    fn default() -> Self {
        let runtime = tokio::runtime::Runtime::new().unwrap();
        
        Self {
            agent_client: AgentClient::with_auto_discovery(),
            agent_manager: Arc::new(TokioMutex::new(AgentManager::new())),
            providers: Vec::new(),
            config: AppConfig::default(),
            prefs: AppPreferences::default(),
            agent_info: None,
            agent_status: AgentStatus {
                is_running: false,
                port: http_client::get_agent_port(),
                message: "Checking...".to_string(),
            },
            last_refresh: None,
            error_message: None,
            settings_open: false,
            settings_viewport_id: egui::ViewportId::from_hash_of("settings_window"),
            selected_tab: 0,
            load_result: Arc::new(Mutex::new(None)),
            background_result: Arc::new(Mutex::new(None)),
            is_refreshing: false,
            is_starting_agent: false,
            runtime,
            debug_log: Vec::new(),
            tray_manager: TrayManager::new(),
            tray_receiver: None,
            minimized_to_tray: false,
            github_auth_status: None,
            github_flow_state: None,
            github_polling: false,
            discovered_providers: Vec::new(),
            history: Vec::new(),
            raw_responses: Vec::new(),
            loading_providers: false,
            loading_history: false,
            loading_github_status: false,
            expanded_sub_providers: {
                let mut set = HashSet::new();
                set.insert("antigravity".to_string());
                set
            },
            expanded_groups: {
                let mut set = HashSet::new();
                set.insert("quota".to_string());
                set.insert("paygo".to_string());
                set
            },
            provider_icons: ProviderIcons::new(),
            last_poll: None,
        }
    }
}

impl AICApp {
    fn log(&mut self, msg: &str) {
        let timestamp = chrono::Local::now().format("%H:%M:%S%.3f");
        let entry = format!("[{}] {}", timestamp, msg);
        self.debug_log.push(entry);
        if self.debug_log.len() > 50 {
            self.debug_log.remove(0);
        }
        log::info!("{}", msg);
    }

    fn get_progress_color(&self, percentage: f64) -> egui::Color32 {
        if percentage >= self.config.color_threshold_red as f64 {
            egui::Color32::from_rgb(220, 20, 60)  // Crimson - #DC143C (same as C# Brushes.Crimson)
        } else if percentage >= self.config.color_threshold_yellow as f64 {
            egui::Color32::from_rgb(255, 215, 0)  // Gold - #FFD700 (same as C# Brushes.Gold)
        } else {
            egui::Color32::from_rgb(60, 179, 113)  // MediumSeaGreen - #3CB371 (same as C# Brushes.MediumSeaGreen)
        }
    }

    fn update_impl(&mut self, ctx: &egui::Context) {
        if self.config.auto_refresh && self.agent_status.is_running {
            ctx.request_repaint_after(Duration::from_secs(REFRESH_INTERVAL_SECS));
        }

        // Poll for incremental updates every 2 seconds if agent is running
        if self.agent_status.is_running {
            let should_poll = match self.last_poll {
                None => true,
                Some(last) => last.elapsed().as_secs() >= POLL_INTERVAL_SECS,
            };
            
            if should_poll && !self.is_refreshing {
                self.last_poll = Some(Instant::now());
                self.poll_for_updates(ctx);
            }
        }

        if let Some(ref receiver) = self.tray_receiver {
            if let Ok(event) = receiver.try_recv() {
                match event {
                    TrayEvent::Show => {
                        self.minimized_to_tray = false;
                        ctx.send_viewport_cmd(egui::ViewportCommand::Visible(true));
                        ctx.send_viewport_cmd(egui::ViewportCommand::Focus);
                    }
                    TrayEvent::Quit => {
                        ctx.send_viewport_cmd(egui::ViewportCommand::Close);
                    }
                }
            }
        }

        let mut load_data: Option<LoadResult> = None;
        if let Ok(mut result) = self.load_result.try_lock() {
            if let Some(data) = result.take() {
                load_data = Some(data);
            }
        }
        
        if let Some(data) = load_data {
            self.log(&format!("Received {} providers", data.providers.len()));
            for p in &data.providers {
                let details_count = p.details.as_ref().map(|d| d.len()).unwrap_or(0);
                self.log(&format!("  {} (quota={}, payment={}, details={})", 
                    p.provider_id, p.is_quota_based, p.payment_type, details_count));
            }
            self.providers = data.providers;
            self.agent_info = data.agent_info;
            self.agent_status = data.agent_status;
            self.error_message = data.error;
            self.is_refreshing = false;
            self.is_starting_agent = false;
            self.last_refresh = Some(Instant::now());
        }

        if let Ok(mut guard) = self.background_result.try_lock() {
            if let Some(result) = guard.take() {
                match result {
                    BackgroundResult::Providers(p) => {
                        self.discovered_providers = p;
                        self.loading_providers = false;
                    }
                    BackgroundResult::History(h) => {
                        self.history = h;
                        self.loading_history = false;
                    }
                    BackgroundResult::GithubStatus(s) => {
                        self.github_auth_status = Some(s);
                        self.loading_github_status = false;
                    }
                }
            }
        }

        self.setup_styles(ctx);
        
        if self.minimized_to_tray {
            return;
        }
        
        egui::TopBottomPanel::top("header")
            .exact_height(28.0)
            .show(ctx, |ui| {
                self.render_header(ui, ctx);
            });

        egui::TopBottomPanel::bottom("footer")
            .exact_height(32.0)
            .show(ctx, |ui| {
                self.render_footer(ui, ctx);
            });

        if self.settings_open {
            self.render_settings_window(ctx);
        }

        egui::CentralPanel::default().show(ctx, |ui| {
            self.render_content(ui, ctx);
        });
    }

    fn render_header(&mut self, ui: &mut egui::Ui, _ctx: &egui::Context) {
        egui::Frame::default()
            .fill(egui::Color32::from_rgb(37, 37, 38))
            .inner_margin(egui::vec2(8.0, 4.0))
            .show(ui, |ui| {
                ui.horizontal(|ui| {
                    ui.label(egui::RichText::new("AI Consumption Tracker").size(12.0).strong().color(egui::Color32::from_rgb(255, 255, 255)));
                    
                    if let Some(info) = &self.agent_info {
                        ui.label(egui::RichText::new(format!("v{}", info.version)).size(9.0).color(egui::Color32::from_rgb(136, 136, 136)));
                    }
                    
                    ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                        if self.config.privacy_mode {
                            ui.label(egui::RichText::new("\u{1F512}").size(14.0).color(egui::Color32::from_rgb(170, 170, 170)));
                        }
                    });
                });
            });
    }

    fn render_footer(&mut self, ui: &mut egui::Ui, ctx: &egui::Context) {
        egui::Frame::default()
            .fill(egui::Color32::from_rgb(30, 30, 30))
            .inner_margin(egui::vec2(8.0, 6.0))
            .show(ui, |ui| {
                ui.horizontal(|ui| {
                    ui.checkbox(&mut self.config.show_all, egui::RichText::new("Show All").size(11.0));
                    
                    let status_color = if self.agent_status.is_running {
                        egui::Color32::from_rgb(0, 204, 106)  // #00CC6A - green
                    } else if self.is_starting_agent || self.is_refreshing {
                        egui::Color32::from_rgb(255, 214, 0)  // #FFD600 - yellow
                    } else {
                        egui::Color32::from_rgb(255, 23, 68)  // #FF1744 - red
                    };
                    
                    ui.add_space(8.0);
                    egui::Frame::default()
                        .fill(status_color)
                        .rounding(egui::Rounding::same(4.0))
                        .inner_margin(egui::vec2(4.0, 2.0))
                        .show(ui, |ui| {
                            ui.label(egui::RichText::new(&self.agent_status.message).size(9.0).color(egui::Color32::from_rgb(0, 0, 0)));
                        });
                    
                    if let Some(time) = self.last_refresh {
                        let elapsed = time.elapsed().as_secs();
                        ui.label(egui::RichText::new(format!("{}s ago", elapsed)).size(9.0).color(egui::Color32::from_rgb(136, 136, 136)));
                    }
                    
                    ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                        let settings_btn = egui::Button::new(egui::RichText::new("\u{2699}").size(16.0))
                            .fill(egui::Color32::TRANSPARENT)
                            .frame(false);
                        if ui.add(settings_btn).on_hover_text("Settings").clicked() {
                            self.settings_open = !self.settings_open;
                        }
                        
                        let refresh_text = if self.is_refreshing { "..." } else { "\u{21BB}" };
                        let refresh_btn = egui::Button::new(egui::RichText::new(refresh_text).size(16.0))
                            .fill(egui::Color32::TRANSPARENT)
                            .frame(false);
                        if ui.add(refresh_btn).on_hover_text("Refresh").clicked() && !self.is_refreshing {
                            self.trigger_load(ctx);
                        }
                        
                        let agent_btn = egui::Button::new(egui::RichText::new("A").size(14.0).strong())
                            .fill(egui::Color32::TRANSPARENT)
                            .frame(false);
                        if ui.add(agent_btn).on_hover_text("Agent Management").clicked() {
                            self.selected_tab = 2;  // Switch to Agent tab
                            self.settings_open = true;
                        }
                    });
                });
            });
    }

    fn render_content(&mut self, ui: &mut egui::Ui, ctx: &egui::Context) {
        if !self.agent_status.is_running && !self.is_refreshing && !self.is_starting_agent {
            ui.centered_and_justified(|ui| {
                ui.vertical(|ui| {
                    ui.label("Agent not running");
                    ui.add_space(8.0);
                    if ui.button("\u{25B6} Start Agent").clicked() {
                        self.trigger_agent_start(ctx);
                    }
                    ui.add_space(8.0);
                    ui.label(egui::RichText::new("The agent is required to fetch provider usage data.").size(10.0).color(egui::Color32::GRAY));
                });
            });
            return;
        }

        if self.providers.is_empty() && !self.agent_status.is_running {
            ui.centered_and_justified(|ui| {
                ui.vertical(|ui| {
                    ui.spinner();
                    ui.label("Connecting to agent...");
                });
            });
            return;
        }

        if self.providers.is_empty() {
            ui.centered_and_justified(|ui| {
                ui.vertical(|ui| {
                    if self.is_refreshing {
                        ui.spinner();
                        ui.label("Loading providers...");
                    } else {
                        ui.label("No providers configured");
                        ui.add_space(8.0);
                        if ui.button("Open Settings").clicked() {
                            self.settings_open = true;
                            self.selected_tab = 0;
                        }
                    }
                });
            });
            return;
        }

        if let Some(err) = &self.error_message {
            ui.colored_label(egui::Color32::from_rgb(255, 23, 68), format!("Error: {}", err));  // #FF1744
            ui.add_space(4.0);
        }

        egui::ScrollArea::vertical().show(ui, |ui| {
            // Debug: show total providers
            if self.config.show_all {
                ui.label(egui::RichText::new(format!("Total providers: {}", self.providers.len())).size(9.0).color(egui::Color32::from_rgb(136, 136, 136)));
            }
            
            let mut quota_providers: Vec<_> = self.providers.iter()
                .filter(|p| p.is_quota_based || p.payment_type == "credits")
                .collect();
            quota_providers.sort_by(|a, b| a.provider_name.to_lowercase().cmp(&b.provider_name.to_lowercase()));
            
            let mut paygo_providers: Vec<_> = self.providers.iter()
                .filter(|p| !p.is_quota_based && p.payment_type != "credits")
                .collect();
            paygo_providers.sort_by(|a, b| a.provider_name.to_lowercase().cmp(&b.provider_name.to_lowercase()));

            // Plans & Quotas group
            if !quota_providers.is_empty() {
                let group_id = "quota";
                let is_expanded = self.expanded_groups.contains(group_id);
                
                let toggle_text = if is_expanded { "v" } else { ">" };
                let label = egui::RichText::new(format!("{} Plans & Quotas", toggle_text))
                    .size(9.0)
                    .strong()
                    .color(egui::Color32::from_rgb(0, 191, 255));
                
                if ui.label(label).clicked() {
                    if is_expanded {
                        self.expanded_groups.remove(group_id);
                    } else {
                        self.expanded_groups.insert(group_id.to_string());
                    }
                }
                
                if is_expanded {
                    for provider in quota_providers {
                        self.render_provider_compact(ui, provider);
                        // Render sub-providers if this provider has details
                        if let Some(details) = &provider.details {
                            if !details.is_empty() && provider.is_available {
                                let provider_id = provider.provider_id.clone();
                                let provider_name = provider.provider_name.clone();
                                let is_sub_expanded = self.expanded_sub_providers.contains(&provider_id);
                                let sub_count = details.len();
                                
                                let sub_toggle_text = if is_sub_expanded { "v" } else { ">" };
                                let sub_label = egui::RichText::new(format!("  {} {} sub providers for {}", sub_toggle_text, sub_count, provider_name))
                                    .size(9.0)
                                    .color(egui::Color32::from_rgb(128, 128, 128));
                                
                                if ui.label(sub_label).clicked() {
                                    if is_sub_expanded {
                                        self.expanded_sub_providers.remove(&provider_id);
                                    } else {
                                        self.expanded_sub_providers.insert(provider_id.clone());
                                    }
                                }
                                
                                if is_sub_expanded {
                                    self.render_sub_providers(ui, details, &provider_id);
                                }
                            }
                        }
                    }
                }
            }

            // Pay As You Go group
            if !paygo_providers.is_empty() {
                let group_id = "paygo";
                let is_expanded = self.expanded_groups.contains(group_id);
                
                let toggle_text = if is_expanded { "v" } else { ">" };
                let label = egui::RichText::new(format!("{} Pay As You Go", toggle_text))
                    .size(9.0)
                    .strong()
                    .color(egui::Color32::from_rgb(60, 179, 113));
                
                if ui.label(label).clicked() {
                    if is_expanded {
                        self.expanded_groups.remove(group_id);
                    } else {
                        self.expanded_groups.insert(group_id.to_string());
                    }
                }
                
                if is_expanded {
                    for provider in paygo_providers {
                        self.render_provider_compact(ui, provider);
                    }
                }
            }
        });
    }

    fn render_group_header(&self, ui: &mut egui::Ui, title: &str, color: egui::Color32) {
        ui.add_space(8.0);
        ui.horizontal(|ui| {
            ui.label(egui::RichText::new(title).size(9.0).strong().color(color));
            ui.add(egui::Separator::default().horizontal().shrink(1.0));
        });
        ui.add_space(4.0);
    }

    fn render_sub_providers(&self, ui: &mut egui::Ui, details: &[crate::models::ProviderUsageDetail], provider_id: &str) {
        // Get parent provider icon info
        let (_, icon, color_hex) = get_provider_info_egui(provider_id);
        let icon_color = parse_hex_color(color_hex);
        
        for detail in details {
            let used_pct = Self::parse_percentage_from_string(&detail.used);
            let remaining_pct = detail.remaining.unwrap_or(100.0 - used_pct) as f32;
            
            let bar_color = self.get_progress_color(used_pct);
            
            let (rect, _response) = ui.allocate_exact_size(
                egui::vec2(ui.available_width(), 18.0),
                egui::Sense::hover(),
            );
            
            // Indent the rect by 12px from the left
            let rect = egui::Rect::from_min_size(
                egui::pos2(rect.min.x + 12.0, rect.min.y),
                egui::vec2(rect.width() - 12.0, rect.height()),
            );
            
            // Progress bar background - same as main bars
            ui.painter().rect_filled(rect, 2.0, egui::Color32::from_rgb(35, 35, 35));
            
            // Progress bar fill - same as main bars (alpha 100)
            if remaining_pct > 0.0 {
                let progress = (remaining_pct / 100.0).min(1.0) as f32;
                let bar_width = rect.width() * progress;
                let bar_rect = egui::Rect::from_min_size(rect.min, egui::vec2(bar_width, rect.height()));
                
                let mut bar_color_alpha = bar_color;
                bar_color_alpha[3] = 100;
                ui.painter().rect_filled(bar_rect, 2.0, bar_color_alpha);
            }
            
            // Draw small icon box
            let icon_rect = egui::Rect::from_min_size(
                egui::pos2(rect.min.x + 2.0, rect.min.y + 3.0),
                egui::vec2(12.0, 12.0),
            );
            ui.painter().rect_filled(icon_rect, 2.0, icon_color.gamma_multiply(0.7));
            
            // Draw icon letter
            let icon_text_pos = egui::pos2(icon_rect.min.x + 3.0, icon_rect.min.y + 1.0);
            ui.painter().text(
                icon_text_pos,
                egui::Align2::LEFT_TOP,
                icon,
                egui::FontId::proportional(8.0),
                egui::Color32::WHITE,
            );
            
            // Percentage text
            let used_text = format!("{:.0}%", used_pct);
            let text_pos = egui::pos2(rect.min.x + 18.0, rect.min.y + 3.0);
            ui.painter().text(
                text_pos,
                egui::Align2::LEFT_TOP,
                &used_text,
                egui::FontId::proportional(9.0),
                bar_color,
            );
            
            // Name in the middle
            let name_pos = egui::pos2(rect.min.x + 40.0, rect.min.y + 3.0);
            ui.painter().text(
                name_pos,
                egui::Align2::LEFT_TOP,
                &detail.name,
                egui::FontId::proportional(9.0),
                egui::Color32::from_rgb(230, 230, 230),
            );
            
            // Status on the right
            let status_text = format!("{:.0}%", remaining_pct);
            let status_pos = egui::pos2(rect.max.x - 4.0, rect.min.y + 3.0);
            ui.painter().text(
                status_pos,
                egui::Align2::RIGHT_TOP,
                &status_text,
                egui::FontId::proportional(9.0),
                egui::Color32::from_rgb(200, 200, 200),
            );
        }
    }

    fn parse_percentage_from_string(s: &str) -> f64 {
        if let Some(cap) = s.chars().filter(|c| c.is_ascii_digit()).collect::<String>().parse::<f64>().ok() {
            cap
        } else {
            0.0
        }
    }

    fn render_provider_compact(&self, ui: &mut egui::Ui, provider: &ProviderUsage) {
        let (rect, response) = ui.allocate_exact_size(
            egui::vec2(ui.available_width(), 24.0),
            egui::Sense::hover(),
        );

        let bg_color = if provider.is_available {
            egui::Color32::from_rgb(35, 35, 35)  // #232323
        } else {
            egui::Color32::from_rgb(30, 30, 30)  // #1E1E1E
        };
        
        ui.painter().rect_filled(rect, 2.0, bg_color);

        if provider.is_available && provider.usage_percentage > 0.0 {
            let progress = (provider.usage_percentage / 100.0).min(1.0) as f32;
            let bar_color = self.get_progress_color(provider.usage_percentage);
            let bar_width = rect.width() * progress;
            let bar_rect = egui::Rect::from_min_size(rect.min, egui::vec2(bar_width, rect.height()));
            
            let mut bar_color_alpha = bar_color;
            bar_color_alpha[3] = 100;
            ui.painter().rect_filled(bar_rect, 2.0, bar_color_alpha);
        }

        // Draw icon - try SVG first, fall back to letter
        let icon_rect = egui::Rect::from_min_size(
            egui::pos2(rect.min.x + 4.0, rect.min.y + 4.0),
            egui::vec2(16.0, 16.0),
        );
        
        if let Some(texture_id) = self.provider_icons.get_or_load(ui.ctx(), &provider.provider_id) {
            let uv = egui::Rect::from_min_max(egui::pos2(0.0, 0.0), egui::pos2(1.0, 1.0));
            ui.painter().image(texture_id, icon_rect, uv, egui::Color32::WHITE);
        } else {
            // Fall back to colored letter icon
            let (_, icon, color_hex) = get_provider_info_egui(&provider.provider_id);
            let icon_color = parse_hex_color(color_hex);
            ui.painter().rect_filled(icon_rect, 2.0, icon_color);
            let icon_text_pos = egui::pos2(icon_rect.min.x + 5.0, icon_rect.min.y + 2.0);
            ui.painter().text(
                icon_text_pos,
                egui::Align2::LEFT_TOP,
                icon,
                egui::FontId::proportional(10.0),
                egui::Color32::WHITE,
            );
        }

        let mut name = provider.provider_name.clone();
        if !provider.account_name.is_empty() && !self.config.privacy_mode {
            name = format!("{} [{}]", provider.provider_name, provider.account_name);
        } else if !provider.account_name.is_empty() && self.config.privacy_mode {
            name = format!("{} [***]", provider.provider_name);
        }

        let text_color = if provider.is_available {
            egui::Color32::from_rgb(255, 255, 255)  // White
        } else {
            egui::Color32::from_rgb(136, 136, 136)  // Gray
        };

        let name_pos = egui::pos2(rect.min.x + 24.0, rect.min.y + 6.0);
        ui.painter().text(
            name_pos,
            egui::Align2::LEFT_TOP,
            &name,
            egui::FontId::proportional(11.0),
            text_color,
        );

        let status_text = if !provider.is_available {
            "N/A".to_string()
        } else if provider.usage_unit == "Status" {
            "OK".to_string()
        } else {
            let pct = format!("{:.0}%", provider.usage_percentage);
            if self.config.privacy_mode {
                pct
            } else if provider.cost_used > 0.0 {
                format!("{} (${:.2})", pct, provider.cost_used)
            } else {
                pct
            }
        };

        let status_color = if provider.is_available {
            egui::Color32::from_rgb(200, 200, 200)  // Secondary text
        } else {
            egui::Color32::from_rgb(136, 136, 136)  // Muted
        };

        let status_pos = egui::pos2(rect.max.x - 8.0, rect.min.y + 6.0);
        ui.painter().text(
            status_pos,
            egui::Align2::RIGHT_TOP,
            &status_text,
            egui::FontId::proportional(10.0),
            status_color,
        );

        response.on_hover_text(format!(
            "Provider: {}\nUsage: {:.1}%\nCost: ${:.2}\nAvailable: {}",
            provider.provider_name,
            provider.usage_percentage,
            provider.cost_used,
            provider.is_available
        ));
    }

    fn render_settings_window(&mut self, ctx: &egui::Context) {
        if !self.settings_open {
            return;
        }
        
        let mut settings_open = self.settings_open;
        let mut selected_tab = self.selected_tab;
        let viewport_id = self.settings_viewport_id;
        
        ctx.show_viewport_immediate(
            viewport_id,
            egui::ViewportBuilder::default()
                .with_title("Settings - AI Consumption Tracker")
                .with_inner_size([550.0, 650.0])
                .with_min_inner_size([400.0, 400.0])
                .with_resizable(true),
            |ctx, class| {
                assert!(
                    class == egui::ViewportClass::Immediate,
                    "This egui backend doesn't support multiple viewports"
                );
                
                egui::CentralPanel::default().show(ctx, |ui| {
                    ui.horizontal(|ui| {
                        let tabs = ["Providers", "Layout", "Updates", "History", "Fonts", "Agent"];
                        for (i, tab) in tabs.iter().enumerate() {
                            if ui.selectable_label(selected_tab == i, *tab).clicked() {
                                selected_tab = i;
                            }
                        }
                    });
                    ui.separator();
                    
                    match selected_tab {
                        0 => self.render_providers_tab(ui, ctx),
                        1 => self.render_layout_tab(ui),
                        2 => self.render_updates_tab(ui),
                        3 => self.render_history_tab(ui),
                        4 => self.render_fonts_tab(ui),
                        5 => self.render_agent_tab(ui, ctx),
                        _ => {}
                    }
                });
                
                if ctx.input(|i| i.viewport().close_requested()) {
                    settings_open = false;
                }
            },
        );
        
        self.settings_open = settings_open;
        self.selected_tab = selected_tab;
        
        if selected_tab == 0 && self.discovered_providers.is_empty() && !self.loading_providers {
            self.load_discovered_providers(ctx);
        } else if selected_tab == 3 && self.history.is_empty() && !self.loading_history {
            self.load_history(ctx);
        }
    }

    fn poll_for_updates(&mut self, _ctx: &egui::Context) {
        // Don't start a new poll if one is already in progress
        if self.is_refreshing {
            return;
        }
        
        let client = self.agent_client.clone();
        let result = Arc::clone(&self.load_result);
        
        self.runtime.spawn(async move {
            match client.get_usage().await {
                Ok(providers) => {
                    if let Ok(mut r) = result.lock() {
                        *r = Some(LoadResult {
                            providers,
                            agent_info: None,
                            agent_status: AgentStatus {
                                is_running: true,
                                port: client.port(),
                                message: "Connected".to_string(),
                            },
                            error: None,
                        });
                    }
                }
                Err(_) => {}
            }
        });
    }

    fn load_discovered_providers(&mut self, ctx: &egui::Context) {
        if !self.discovered_providers.is_empty() || self.loading_providers {
            return;
        }
        
        self.loading_providers = true;
        
        let client = self.agent_client.clone();
        let ctx_clone = ctx.clone();
        let result = Arc::clone(&self.background_result);
        
        self.runtime.spawn(async move {
            match client.get_providers().await {
                Ok(p) => {
                    if let Ok(mut guard) = result.lock() {
                        *guard = Some(BackgroundResult::Providers(p));
                    }
                }
                Err(_) => {
                    if let Ok(mut guard) = result.lock() {
                        *guard = Some(BackgroundResult::Providers(Vec::new()));
                    }
                }
            }
            ctx_clone.request_repaint();
        });
    }

    fn load_history(&mut self, ctx: &egui::Context) {
        if !self.history.is_empty() || self.loading_history {
            return;
        }
        
        self.loading_history = true;
        
        let client = self.agent_client.clone();
        let ctx_clone = ctx.clone();
        let result = Arc::clone(&self.background_result);
        
        self.runtime.spawn(async move {
            match client.get_history(Some(50)).await {
                Ok(h) => {
                    if let Ok(mut guard) = result.lock() {
                        *guard = Some(BackgroundResult::History(h));
                    }
                }
                Err(_) => {
                    if let Ok(mut guard) = result.lock() {
                        *guard = Some(BackgroundResult::History(Vec::new()));
                    }
                }
            }
            ctx_clone.request_repaint();
        });
    }

    fn render_providers_tab(&mut self, ui: &mut egui::Ui, _ctx: &egui::Context) {
        if self.discovered_providers.is_empty() {
            ui.label("Loading providers...");
            return;
        }
        
        // Sort providers alphabetically by name (matching Tauri app)
        let mut sorted_providers = self.discovered_providers.clone();
        sorted_providers.sort_by(|a, b| {
            let name_a = get_provider_display_name(a);
            let name_b = get_provider_display_name(b);
            name_a.cmp(&name_b)
        });
        
        egui::ScrollArea::vertical()
            .auto_shrink([false; 2])
            .stick_to_bottom(true)
            .min_scrolled_height(0.0)
            .show(ui, |ui| {
                for provider in &sorted_providers {
                    let provider_id = provider.get("provider_id").and_then(|p| p.as_str()).unwrap_or("unknown");
                    let (name, icon, color) = get_provider_info_egui(provider_id);
                    
                    // Skip unknown providers (not in our supported list)
                    if name == "Unknown" {
                        continue;
                    }
                    
                    let api_key = provider.get("api_key").and_then(|k| k.as_str()).unwrap_or("");
                    let show_in_tray = provider.get("show_in_tray").and_then(|s| s.as_bool()).unwrap_or(true);
                    
                    let has_key = !api_key.is_empty();
                    let auth_source = provider.get("auth_source").and_then(|a| a.as_str()).unwrap_or("");
                    let auth_display = match auth_source {
                        "Environment Variable" | "Environment" => "Env",
                        "AI Consumption Tracker" => "AICT", 
                        "GitHub OAuth" => "OAuth",
                        _ => if auth_source.is_empty() { "-" } else { auth_source },
                    };
                    
                    // Full width card - matching Tauri styling
                    egui::Frame::default()
                        .fill(egui::Color32::from_rgb(45, 45, 48))
                        .stroke(egui::Stroke::new(1.0, egui::Color32::from_rgb(51, 51, 51)))
                        .rounding(egui::Rounding::same(4.0))
                        .inner_margin(egui::vec2(12.0, 8.0))
                        .show(ui, |ui| {
                            // Check if this is antigravity (no API key needed) - moved outside for wider scope
                            let is_antigravity = provider_id == "antigravity";
                            let is_connected = is_antigravity && provider.get("is_available").and_then(|v| v.as_bool()).unwrap_or(false);
                            
                            // Header row - icon, name on left, actions on right
                            ui.horizontal(|ui| {
                                // Left side: icon and name
                                ui.horizontal(|ui| {
                                    // Provider icon
                                    let icon_color = parse_hex_color(color);
                                    egui::Frame::default()
                                        .fill(icon_color)
                                        .rounding(egui::Rounding::same(4.0))
                                        .inner_margin(egui::vec2(6.0, 4.0))
                                        .show(ui, |ui| {
                                            ui.label(egui::RichText::new(icon).size(12.0).color(egui::Color32::WHITE).strong());
                                        });
                                    
                                    ui.label(egui::RichText::new(name).size(13.0));
                                });
                                
                                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                                    // Right side: auth source, tray, status
                                    
                                    // Status badge
                                    let status_color = if has_key || is_connected {
                                        egui::Color32::from_rgb(0, 204, 106)  // green
                                    } else {
                                        egui::Color32::from_rgb(136, 136, 136)  // gray
                                    };
                                    let status_text = if has_key { 
                                        "Active" 
                                    } else if is_connected {
                                        "Connected"
                                    } else { 
                                        "Inactive" 
                                    };
                                    
                                    egui::Frame::default()
                                        .fill(status_color)
                                        .rounding(egui::Rounding::same(3.0))
                                        .inner_margin(egui::vec2(8.0, 4.0))
                                        .show(ui, |ui| {
                                            ui.label(egui::RichText::new(status_text).size(10.0).color(egui::Color32::BLACK));
                                        });
                                    
                                    ui.add_space(8.0);
                                    
                                    // Tray checkbox
                                    ui.label(egui::RichText::new("Tray").size(11.0));
                                    let mut tray_enabled = show_in_tray;
                                    ui.checkbox(&mut tray_enabled, "");
                                    
                                    ui.add_space(8.0);
                                    
                                    // Auth source badge
                                    egui::Frame::default()
                                        .fill(egui::Color32::from_rgb(30, 30, 30))
                                        .rounding(egui::Rounding::same(3.0))
                                        .inner_margin(egui::vec2(8.0, 4.0))
                                        .show(ui, |ui| {
                                            ui.label(egui::RichText::new(auth_display).size(10.0).color(egui::Color32::from_rgb(170, 170, 170)));
                                        });
                                });
                            });
                            
                            // API Key row (not shown for antigravity)
                            if !is_antigravity {
                                ui.add_space(6.0);
                                ui.horizontal(|ui| {
                                    ui.label(egui::RichText::new("API Key").size(11.0).color(egui::Color32::from_rgb(170, 170, 170)));
                                    
                                    let mut api_key_display = if api_key.is_empty() {
                                        "".to_string()
                                    } else if self.config.privacy_mode {
                                        "••••••••".to_string()
                                    } else {
                                        api_key.to_string()
                                    };
                                    
                                    ui.add(egui::TextEdit::singleline(&mut api_key_display)
                                        .desired_width(ui.available_width() * 0.6)
                                        .hint_text("Enter API key"));
                                });
                            } else {
                                // Show status for antigravity
                                ui.add_space(6.0);
                                let status_msg = if is_connected { "Running (Connected)" } else { "Not Running" };
                                ui.label(egui::RichText::new(status_msg).size(11.0).color(egui::Color32::from_rgb(136, 136, 136)));
                                
                                // Show sub-trays (Individual Quota Icons) for antigravity
                                if let Some(usage) = self.providers.iter().find(|p| p.provider_id == "antigravity") {
                                    if let Some(details) = &usage.details {
                                        if !details.is_empty() {
                                            ui.add_space(10.0);
                                            ui.separator();
                                            ui.add_space(8.0);
                                            ui.label(egui::RichText::new("Individual Quota Icons:").size(11.0).strong().color(egui::Color32::from_rgb(136, 136, 136)));
                                            
                                            let enabled_sub_trays: Vec<String> = provider.get("enabled_sub_trays")
                                                .and_then(|v| v.as_array())
                                                .map(|arr| {
                                                    arr.iter().filter_map(|v| v.as_str().map(String::from)).collect()
                                                })
                                                .unwrap_or_default();
                                            
                                            for detail in details {
                                                let mut enabled = enabled_sub_trays.contains(&detail.name);
                                                let response = ui.checkbox(&mut enabled, egui::RichText::new(&detail.name).size(11.0).color(egui::Color32::from_rgb(204, 204, 204)));
                                                
                                                // Save when checkbox is toggled
                                                if response.changed() {
                                                    let mut new_enabled_trays = enabled_sub_trays.clone();
                                                    if enabled {
                                                        if !new_enabled_trays.contains(&detail.name) {
                                                            new_enabled_trays.push(detail.name.clone());
                                                        }
                                                    } else {
                                                        new_enabled_trays.retain(|t| t != &detail.name);
                                                    }
                                                    
                                                    // Save to agent
                                                    let api_key = provider.get("api_key").and_then(|k| k.as_str()).unwrap_or("").to_string();
                                                    let show_in_tray = provider.get("show_in_tray").and_then(|s| s.as_bool()).unwrap_or(true);
                                                    let auth_source = provider.get("auth_source").and_then(|a| a.as_str()).unwrap_or("").to_string();
                                                    
                                                    let config = ProviderConfig {
                                                        provider_id: "antigravity".to_string(),
                                                        api_key,
                                                        config_type: "quota".to_string(),
                                                        payment_type: "credits".to_string(),
                                                        limit: None,
                                                        base_url: None,
                                                        show_in_tray,
                                                        enabled_sub_trays: new_enabled_trays,
                                                        auth_source,
                                                    };
                                                    
                                                    let client = self.agent_client.clone();
                                                    self.runtime.spawn(async move {
                                                        if let Err(e) = client.save_provider_config(&config).await {
                                                            log::error!("Failed to save sub-tray config: {}", e);
                                                        }
                                                    });
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        });
                    
                    ui.add_space(4.0);
                }
            });
    }

    fn render_layout_tab(&mut self, ui: &mut egui::Ui) {
        ui.checkbox(&mut self.config.privacy_mode, "Privacy Mode");
        ui.checkbox(&mut self.config.show_all, "Show All Providers");
        ui.checkbox(&mut self.config.auto_refresh, "Auto Refresh");
        ui.checkbox(&mut self.config.auto_start_agent, "Auto Start Agent");
        ui.checkbox(&mut self.config.always_on_top, "Always on Top");
        ui.checkbox(&mut self.config.compact_mode, "Compact Mode");
        
        ui.add_space(8.0);
        ui.label("Color Thresholds:");
        ui.horizontal(|ui| {
            ui.label("Yellow:");
            ui.add(egui::Slider::new(&mut self.config.color_threshold_yellow, 0..=100));
        });
        ui.horizontal(|ui| {
            ui.label("Red:");
            ui.add(egui::Slider::new(&mut self.config.color_threshold_red, 0..=100));
        });
    }

    fn render_updates_tab(&mut self, ui: &mut egui::Ui) {
        ui.label("Updates");
        ui.add_space(8.0);
        
        if let Some(info) = &self.agent_info {
            ui.label(egui::RichText::new(format!("Current version: {}", info.version)).size(12.0));
        }
        
        ui.add_space(16.0);
        ui.label(egui::RichText::new("Check for updates:").size(11.0).color(egui::Color32::from_rgb(170, 170, 170)));
        
        if ui.button("Check for Updates").clicked() {
            // Placeholder - would check for updates
        }
    }

    fn render_fonts_tab(&mut self, ui: &mut egui::Ui) {
        ui.label("Fonts");
        ui.add_space(8.0);
        ui.label(egui::RichText::new("Font settings will be available in a future update.").size(11.0).color(egui::Color32::from_rgb(170, 170, 170)));
    }

    fn render_agent_tab(&mut self, ui: &mut egui::Ui, ctx: &egui::Context) {
        ui.label(egui::RichText::new("Connection Information").strong().size(12.0));
        ui.add_space(8.0);
        
        let status_color = if self.agent_status.is_running {
            egui::Color32::from_rgb(0, 204, 106)
        } else {
            egui::Color32::from_rgb(255, 23, 68)
        };
        
        egui::Frame::default()
            .fill(egui::Color32::from_rgb(45, 45, 48))
            .rounding(egui::Rounding::same(4.0))
            .inner_margin(egui::vec2(12.0, 8.0))
            .show(ui, |ui| {
                ui.horizontal(|ui| {
                    egui::Frame::default()
                        .fill(status_color)
                        .rounding(egui::Rounding::same(4.0))
                        .inner_margin(egui::vec2(8.0, 4.0))
                        .show(ui, |ui| {
                            ui.label(egui::RichText::new(if self.agent_status.is_running { "Running" } else { "Stopped" })
                                .size(10.0)
                                .color(egui::Color32::BLACK));
                        });
                    ui.label(&self.agent_status.message);
                });
                
                ui.add_space(8.0);
                
                if let Some(info) = &self.agent_info {
                    ui.horizontal(|ui| {
                        ui.label(egui::RichText::new("Version:").size(11.0).color(egui::Color32::from_rgb(170, 170, 170)));
                        ui.label(egui::RichText::new(&info.version).size(11.0));
                    });
                    ui.horizontal(|ui| {
                        ui.label(egui::RichText::new("Port:").size(11.0).color(egui::Color32::from_rgb(170, 170, 170)));
                        ui.label(egui::RichText::new(self.agent_status.port.to_string()).size(11.0));
                    });
                    ui.horizontal(|ui| {
                        ui.label(egui::RichText::new("Uptime:").size(11.0).color(egui::Color32::from_rgb(170, 170, 170)));
                        let uptime_secs = info.uptime_seconds;
                        let hours = uptime_secs / 3600;
                        let mins = (uptime_secs % 3600) / 60;
                        let secs = uptime_secs % 60;
                        let uptime_str = if hours > 0 {
                            format!("{}h {}m {}s", hours, mins, secs)
                        } else if mins > 0 {
                            format!("{}m {}s", mins, secs)
                        } else {
                            format!("{}s", secs)
                        };
                        ui.label(egui::RichText::new(uptime_str).size(11.0));
                    });
                    ui.horizontal(|ui| {
                        ui.label(egui::RichText::new("Path:").size(11.0).color(egui::Color32::from_rgb(170, 170, 170)));
                        ui.label(egui::RichText::new(&info.agent_path).size(10.0).color(egui::Color32::from_rgb(136, 136, 136)));
                    });
                    ui.horizontal(|ui| {
                        ui.label(egui::RichText::new("Database:").size(11.0).color(egui::Color32::from_rgb(170, 170, 170)));
                        ui.label(egui::RichText::new(&info.database_path).size(10.0).color(egui::Color32::from_rgb(136, 136, 136)));
                    });
                }
            });
        
        ui.add_space(12.0);
        
        // Auto-start option
        ui.horizontal(|ui| {
            let mut auto_start = self.config.auto_start_agent;
            if ui.checkbox(&mut auto_start, "Auto-start agent on launch").changed() {
                self.config.auto_start_agent = auto_start;
            }
        });
        
        ui.add_space(12.0);
        
        ui.horizontal(|ui| {
            if self.agent_status.is_running {
                if ui.button("Restart Agent").clicked() && !self.is_starting_agent {
                    let manager = Arc::clone(&self.agent_manager);
                    let ctx_clone = ctx.clone();
                    let client = self.agent_client.clone();
                    self.runtime.spawn(async move {
                        {
                            let mut m = manager.lock().await;
                            m.kill();
                        }
                        tokio::time::sleep(std::time::Duration::from_secs(1)).await;
                        if agent::wait_for_agent_ready(&client, 5).await {
                            ctx_clone.request_repaint();
                        }
                    });
                    self.trigger_agent_start(ctx);
                }
                if ui.button("Stop Agent").clicked() {
                    let manager = Arc::clone(&self.agent_manager);
                    self.runtime.spawn(async move {
                        let mut m = manager.lock().await;
                        m.kill();
                    });
                    self.agent_status.is_running = false;
                    self.agent_status.message = "Stopped".to_string();
                }
            } else {
                if ui.button("Start Agent").clicked() && !self.is_starting_agent {
                    self.trigger_agent_start(ctx);
                }
            }
            if ui.button("Refresh").clicked() && !self.is_refreshing {
                self.trigger_load(ctx);
            }
        });
        
        ui.add_space(16.0);
        
        ui.label(egui::RichText::new("GitHub Copilot Authentication").strong().size(12.0));
        ui.separator();
        ui.add_space(8.0);
        
        self.render_github_auth_section(ui, ctx);
    }

    fn render_github_auth_section(&mut self, ui: &mut egui::Ui, ctx: &egui::Context) {
        if self.github_auth_status.is_none() && self.agent_status.is_running && !self.loading_github_status {
            self.loading_github_status = true;
            
            let client = self.agent_client.clone();
            let ctx_clone = ctx.clone();
            let result = Arc::clone(&self.background_result);
            
            self.runtime.spawn(async move {
                match client.get_github_auth_status().await {
                    Ok(s) => {
                        if let Ok(mut guard) = result.lock() {
                            *guard = Some(BackgroundResult::GithubStatus(s));
                        }
                    }
                    Err(_) => {
                        if let Ok(mut guard) = result.lock() {
                            *guard = Some(BackgroundResult::GithubStatus(GitHubAuthStatus {
                                is_authenticated: false,
                                username: None,
                                token_invalid: false,
                            }));
                        }
                    }
                }
                ctx_clone.request_repaint();
            });
        }
        
        if let Some(status) = &self.github_auth_status {
            if status.is_authenticated {
                ui.horizontal(|ui| {
                    ui.label("Status:");
                    egui::Frame::default()
                        .fill(egui::Color32::GREEN)
                        .rounding(egui::Rounding::same(4.0))
                        .inner_margin(egui::vec2(4.0, 2.0))
                        .show(ui, |ui| {
                            ui.label("Authenticated");
                        });
                });
                
                if let Some(username) = &status.username {
                    let display_name = if self.config.privacy_mode {
                        "***"
                    } else {
                        username.as_str()
                    };
                    ui.label(format!("Username: {}", display_name));
                }
                
                if ui.button("Logout").clicked() {
                    let client = self.agent_client.clone();
                    let ctx_clone = ctx.clone();
                    self.github_auth_status = None;
                    self.runtime.spawn(async move {
                        let _ = client.logout_github().await;
                        ctx_clone.request_repaint();
                    });
                }
            } else if status.token_invalid {
                ui.colored_label(egui::Color32::RED, "Token Invalid - Please Re-authenticate");
                self.render_github_login_button(ui, ctx);
            } else {
                ui.label("Not authenticated");
                self.render_github_login_button(ui, ctx);
            }
        } else {
            ui.label("Checking GitHub authentication status...");
        }
        
        if let Some(flow) = &self.github_flow_state {
            ui.add_space(8.0);
            egui::Frame::default()
                .fill(egui::Color32::from_rgb(50, 50, 50))
                .rounding(egui::Rounding::same(4.0))
                .inner_margin(egui::vec2(8.0, 6.0))
                .show(ui, |ui| {
                    ui.label("Device Flow Active");
                    if let Some(code) = &flow.user_code {
                        ui.label(format!("Code: {}", code));
                        if ui.button("Copy Code").clicked() {
                            ui.output_mut(|o| o.copied_text = code.clone());
                        }
                    }
                    if let Some(uri) = &flow.verification_uri {
                        ui.hyperlink_to("Open Verification URL", uri);
                    }
                    ui.label("Waiting for authorization...");
                });
            
            if ui.button("Cancel").clicked() {
                self.github_flow_state = None;
                self.github_polling = false;
            }
        }
    }

    fn render_github_login_button(&mut self, ui: &mut egui::Ui, ctx: &egui::Context) {
        if ui.button("Login with GitHub").clicked() {
            let client = self.agent_client.clone();
            let ctx_clone = ctx.clone();
            
            self.runtime.spawn(async move {
                if let Ok(flow) = client.initiate_github_device_flow().await {
                    log::info!("GitHub device flow initiated: {:?}", flow);
                }
                ctx_clone.request_repaint();
            });
            
            self.github_polling = true;
        }
    }

    fn render_history_tab(&mut self, ui: &mut egui::Ui) {
        if self.history.is_empty() {
            ui.label("No history data available");
            ui.label("History is recorded when the agent fetches usage data.");
            return;
        }
        
        ui.label(format!("{} history entries", self.history.len()));
        ui.add_space(8.0);
        
        egui::ScrollArea::vertical().max_height(400.0).show(ui, |ui| {
            egui::Grid::new("history_grid")
                .num_columns(5)
                .spacing([10.0, 4.0])
                .show(ui, |ui| {
                    ui.label(egui::RichText::new("Time").strong());
                    ui.label(egui::RichText::new("Provider").strong());
                    ui.label(egui::RichText::new("Cost").strong());
                    ui.label(egui::RichText::new("Requests").strong());
                    ui.label(egui::RichText::new("Tokens").strong());
                    ui.end_row();
                    
                    for entry in &self.history {
                        if let Some(ts) = entry.get("timestamp").and_then(|t| t.as_str()) {
                            ui.label(ts);
                        } else {
                            ui.label("-");
                        }
                        
                        if let Some(provider) = entry.get("provider_name").and_then(|p| p.as_str()) {
                            ui.label(provider);
                        } else {
                            ui.label("-");
                        }
                        
                        if let Some(cost) = entry.get("cost_used").and_then(|c| c.as_f64()) {
                            ui.label(format!("${:.2}", cost));
                        } else {
                            ui.label("-");
                        }
                        
                        if let Some(reqs) = entry.get("requests_count").and_then(|r| r.as_i64()) {
                            ui.label(reqs.to_string());
                        } else {
                            ui.label("-");
                        }
                        
                        if let (Some(in_tok), Some(out_tok)) = (
                            entry.get("tokens_input").and_then(|t| t.as_i64()),
                            entry.get("tokens_output").and_then(|t| t.as_i64()),
                        ) {
                            ui.label(format!("{}/{}", in_tok, out_tok));
                        } else {
                            ui.label("-");
                        }
                        
                        ui.end_row();
                    }
                });
        });
    }

    fn render_debug_tab(&self, ui: &mut egui::Ui) {
        ui.label(egui::RichText::new("Debug Log").strong());
        ui.separator();
        
        egui::ScrollArea::vertical().max_height(250.0).show(ui, |ui| {
            for entry in &self.debug_log {
                ui.label(egui::RichText::new(entry).size(10.0).family(egui::FontFamily::Monospace));
            }
        });
        
        ui.add_space(8.0);
        ui.label(egui::RichText::new("State").strong());
        ui.separator();
        ui.label(format!("is_refreshing: {}", self.is_refreshing));
        ui.label(format!("is_starting_agent: {}", self.is_starting_agent));
        ui.label(format!("providers count: {}", self.providers.len()));
        ui.label(format!("agent_status.is_running: {}", self.agent_status.is_running));
        ui.label(format!("agent_status.port: {}", self.agent_status.port));
        ui.label(format!("agent_status.message: {}", self.agent_status.message));
        ui.label(format!("error_message: {:?}", self.error_message));
    }

    fn render_about_tab(&self, ui: &mut egui::Ui) {
        ui.label(egui::RichText::new("AI Consumption Tracker (egui)").strong());
        ui.label("Version: 0.5.0");
        ui.add_space(8.0);
        
        ui.label(egui::RichText::new("Agent Status").strong());
        if let Some(info) = &self.agent_info {
            ui.label(format!("Version: {}", info.version));
            ui.label(format!("Path: {}", info.agent_path));
            ui.label(format!("Uptime: {} seconds", info.uptime_seconds));
            ui.label(format!("Database: {}", info.database_path));
        } else {
            ui.label("Not connected");
        }
    }

    fn trigger_load(&mut self, ctx: &egui::Context) {
        if self.is_refreshing {
            return;
        }
        
        self.is_refreshing = true;
        self.log("Starting data load...");
        
        let client = self.agent_client.clone();
        let result = Arc::clone(&self.load_result);
        let ctx_clone = ctx.clone();
        let auto_start = self.config.auto_start_agent;
        let agent_manager = Arc::clone(&self.agent_manager);
        
        self.runtime.spawn(async move {
            let mut load_result = LoadResult {
                providers: Vec::new(),
                agent_info: None,
                agent_status: AgentStatus {
                    is_running: false,
                    port: client.port(),
                    message: "Checking...".to_string(),
                },
                error: None,
            };

            match client.check_agent_status().await {
                Ok(status) => {
                    load_result.agent_status = status;
                }
                Err(e) => {
                    load_result.agent_status = AgentStatus {
                        is_running: false,
                        port: client.port(),
                        message: format!("Error: {}", e),
                    };
                }
            }

            if !load_result.agent_status.is_running && auto_start {
                let start_result = {
                    let mut manager = agent_manager.lock().await;
                    manager.start()
                };
                
                match start_result {
                    Ok(true) => {
                        if agent::wait_for_agent_ready(&client, 5).await {
                            load_result.agent_status = AgentStatus {
                                is_running: true,
                                port: client.port(),
                                message: "Agent Connected".to_string(),
                            };
                        } else {
                            load_result.error = Some("Agent started but not responding".to_string());
                        }
                    }
                    Err(e) => {
                        load_result.error = Some(e);
                    }
                    Ok(false) => {}
                }
            }

            if load_result.agent_status.is_running {
                match client.get_usage().await {
                    Ok(providers) => {
                        load_result.providers = providers;
                    }
                    Err(e) => {
                        load_result.error = Some(e.to_string());
                    }
                }

                match client.get_agent_info().await {
                    Ok(info) => {
                        load_result.agent_info = Some(info);
                    }
                    Err(_) => {}
                }
            }

            if let Ok(mut r) = result.lock() {
                *r = Some(load_result);
            }
            
            ctx_clone.request_repaint();
        });
    }

    fn trigger_agent_start(&mut self, ctx: &egui::Context) {
        if self.is_starting_agent {
            return;
        }
        
        self.is_starting_agent = true;
        self.log("Starting agent...");
        
        let client = self.agent_client.clone();
        let result = Arc::clone(&self.load_result);
        let ctx_clone = ctx.clone();
        let agent_manager = Arc::clone(&self.agent_manager);
        
        self.runtime.spawn(async move {
            let start_result = {
                let mut manager = agent_manager.lock().await;
                manager.start()
            };
            
            match start_result {
                Ok(true) => {
                    if agent::wait_for_agent_ready(&client, 5).await {
                        if let Ok(mut r) = result.lock() {
                            *r = Some(LoadResult {
                                providers: Vec::new(),
                                agent_info: None,
                                agent_status: AgentStatus {
                                    is_running: true,
                                    port: client.port(),
                                    message: "Agent Connected".to_string(),
                                },
                                error: None,
                            });
                        }
                        
                        match client.get_usage().await {
                            Ok(providers) => {
                                if let Ok(mut r) = result.lock() {
                                    if let Some(res) = r.as_mut() {
                                        res.providers = providers;
                                    }
                                }
                            }
                            Err(e) => {
                                if let Ok(mut r) = result.lock() {
                                    if let Some(res) = r.as_mut() {
                                        res.error = Some(e.to_string());
                                    }
                                }
                            }
                        }
                        
                        match client.get_agent_info().await {
                            Ok(info) => {
                                if let Ok(mut r) = result.lock() {
                                    if let Some(res) = r.as_mut() {
                                        res.agent_info = Some(info);
                                    }
                                }
                            }
                            Err(_) => {}
                        }
                    }
                }
                Err(e) => {
                    if let Ok(mut r) = result.lock() {
                        *r = Some(LoadResult {
                            providers: Vec::new(),
                            agent_info: None,
                            agent_status: AgentStatus {
                                is_running: false,
                                port: client.port(),
                                message: "Failed".to_string(),
                            },
                            error: Some(e),
                        });
                    }
                }
                Ok(false) => {}
            }
            
            ctx_clone.request_repaint();
        });
    }

    fn setup_styles(&self, ctx: &egui::Context) {
        let mut style = (*ctx.style()).clone();
        
        // Dark mode
        style.visuals.dark_mode = true;
        
        // Background colors - matching Tauri app
        style.visuals.extreme_bg_color = egui::Color32::from_rgb(30, 30, 30);  // #1E1E1E
        style.visuals.panel_fill = egui::Color32::from_rgb(37, 37, 38);        // #252526
        style.visuals.window_fill = egui::Color32::from_rgb(45, 45, 48);        // #2D2D30
        
        // Text colors using override
        style.visuals.override_text_color = Some(egui::Color32::from_rgb(255, 255, 255)); // #FFFFFF
        
        // Widget styling
        style.visuals.widgets.noninteractive.bg_fill = egui::Color32::from_rgb(45, 45, 48);
        style.visuals.widgets.noninteractive.bg_stroke = egui::Stroke::new(1.0, egui::Color32::from_rgb(51, 51, 51));
        
        // Interactive widgets
        style.visuals.widgets.hovered.bg_fill = egui::Color32::from_rgb(60, 60, 60);
        style.visuals.widgets.hovered.bg_stroke = egui::Stroke::new(1.0, egui::Color32::from_rgb(0, 122, 204)); // accent-blue
        
        style.visuals.widgets.active.bg_fill = egui::Color32::from_rgb(0, 122, 204);
        style.visuals.widgets.active.bg_stroke = egui::Stroke::new(1.0, egui::Color32::from_rgb(0, 158, 255));
        
        style.visuals.widgets.open.bg_fill = egui::Color32::from_rgb(50, 50, 50);
        style.visuals.widgets.open.bg_stroke = egui::Stroke::new(1.0, egui::Color32::from_rgb(0, 122, 204));
        
        // Selection
        style.visuals.selection.bg_fill = egui::Color32::from_rgb(0, 80, 160);
        style.visuals.selection.stroke = egui::Stroke::new(1.0, egui::Color32::from_rgb(0, 122, 204));
        
        // Hyperlink
        style.visuals.hyperlink_color = egui::Color32::from_rgb(0, 158, 255);
        
        ctx.set_style(style);
    }
}

impl eframe::App for AICApp {
    fn update(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        self.update_impl(ctx);
    }
}

fn main() -> eframe::Result<()> {
    tracing_subscriber::fmt::init();
    log::info!("Starting AI Consumption Tracker (egui)");

    let icon = load_app_icon();
    
    let mut viewport = egui::ViewportBuilder::default()
        .with_inner_size([420.0, 520.0])
        .with_min_inner_size([350.0, 400.0])
        .with_title("AI Consumption Tracker")
        .with_decorations(true)
        .with_transparent(false)
        .with_always_on_top();
    
    if let Some(icon_data) = icon {
        viewport = viewport.with_icon(icon_data);
    }
    
    let options = eframe::NativeOptions {
        viewport,
        ..Default::default()
    };

    eframe::run_native(
        "AI Consumption Tracker",
        options,
        Box::new(|cc| {
            let mut app = AICApp::default();
            
            log::info!("Icons initialized");
            
            match app.tray_manager.initialize() {
                Ok(rx) => {
                    app.tray_receiver = Some(rx);
                    log::info!("System tray initialized");
                }
                Err(e) => {
                    log::warn!("Failed to initialize system tray: {}", e);
                }
            }
            
            app.log("App initialized, triggering initial load");
            app.trigger_load(&cc.egui_ctx);
            Ok(Box::new(app))
        }),
    )
}

fn load_app_icon() -> Option<egui::IconData> {
    let icon_bytes = include_bytes!("../../aic_app/icons/icon.png");
    
    let img = image::load_from_memory(icon_bytes).ok()?;
    let rgba = img.to_rgba8();
    let size = [rgba.width() as _, rgba.height() as _];
    let rgba = rgba.as_flat_samples();
    
    Some(egui::IconData {
        rgba: rgba.as_slice().to_vec(),
        width: size[0],
        height: size[1],
    })
}

fn get_provider_info_egui(provider_id: &str) -> (&'static str, &'static str, &'static str) {
    match provider_id {
        "github-copilot" => ("GitHub Copilot", "G", "#24292e"),
        "openai" => ("OpenAI", "O", "#10a37f"),
        "claude-code" => ("Claude Code", "C", "#d4a574"),
        "anthropic" => ("Anthropic", "A", "#d4a574"),
        "deepseek" => ("DeepSeek", "D", "#1e80ff"),
        "gemini-cli" => ("Google Gemini", "G", "#4285f4"),
        "google" => ("Google AI", "G", "#4285f4"),
        "kimi" => ("Kimi", "K", "#0066cc"),
        "minimax" => ("MiniMax", "M", "#FF6B35"),
        "xiaomi" => ("Xiaomi", "X", "#FF6900"),
        "antigravity" => ("Antigravity", "A", "#8B5CF6"),
        "openrouter" => ("OpenRouter", "R", "#10B981"),
        "zai" => ("Z.ai", "Z", "#3B82F6"),
        "zai-coding-plan" => ("Z.ai Coding", "Z", "#2563EB"),
        "mistral" => ("Mistral", "M", "#F97316"),
        "opencode-zen" => ("OpenCode", "C", "#EC4899"),
        "synthetic" => ("Synthetic", "S", "#14B8A6"),
        _ => ("Unknown", "?", "#666666"),
    }
}

fn get_provider_display_name(provider: &serde_json::Value) -> String {
    let provider_id = provider.get("provider_id").and_then(|p| p.as_str()).unwrap_or("unknown");
    let (name, _, _) = get_provider_info_egui(provider_id);
    name.to_string()
}

fn parse_hex_color(hex: &str) -> egui::Color32 {
    if hex.len() >= 7 {
        let r = u8::from_str_radix(&hex[1..3], 16).unwrap_or(100);
        let g = u8::from_str_radix(&hex[3..5], 16).unwrap_or(100);
        let b = u8::from_str_radix(&hex[5..7], 16).unwrap_or(100);
        egui::Color32::from_rgb(r, g, b)
    } else {
        egui::Color32::from_rgb(100, 100, 100)
    }
}
