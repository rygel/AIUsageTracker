use aic_core::{AuthenticationManager, ConfigLoader, GitHubAuthService, ProviderUsage};
use clap::{Parser, Subcommand};
use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};
use std::io::{self, Write};
use std::process::Command;
use tracing::debug;

#[derive(Parser)]
    #[command(name = "aic-cli")]
#[command(about = "AI Consumption Tracker CLI")]
struct Cli {
    #[command(subcommand)]
    command: Option<Commands>,

    /// Agent service URL
    #[arg(long, global = true, default_value = "http://localhost:8080")]
    agent_url: String,

    /// Show all providers even if not configured
    #[arg(long, global = true)]
    all: bool,

    /// Output as JSON
    #[arg(long, global = true)]
    json: bool,

    /// Enable debug logging (verbose output)
    #[arg(long, global = true)]
    debug: bool,
}

#[derive(Subcommand)]
enum Commands {
    /// Show usage status
    Status,
    /// List configured providers
    List,
    /// Authenticate with a provider
    Auth {
        /// Provider to authenticate with
        provider: String,
    },
    /// Logout from a provider
    Logout {
        /// Provider to logout from
        provider: String,
    },
    /// Refresh provider usage
    Refresh,
    /// Show historical usage
    History {
        /// Provider ID filter
        #[arg(long)]
        provider_id: Option<String>,
        /// Number of records to show
        #[arg(long, default_value = "10")]
        limit: usize,
    },
    /// Show agent health
    Health,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
struct AgentUsageResponse {
    provider_id: String,
    provider_name: String,
    usage: Option<f64>,
    limit: Option<f64>,
    usage_unit: String,
    is_available: bool,
    is_quota_based: bool,
    last_updated: DateTime<Utc>,
}

#[derive(Debug, Serialize, Deserialize)]
struct HistoricalUsageResponse {
    records: Vec<HistoricalUsageRecord>,
    total_records: usize,
}

#[derive(Debug, Serialize, Deserialize)]
struct HistoricalUsageRecord {
    id: String,
    provider_id: String,
    provider_name: String,
    usage: f64,
    limit: Option<f64>,
    usage_unit: String,
    is_quota_based: bool,
    timestamp: String,
}

impl From<AgentUsageResponse> for ProviderUsage {
    fn from(u: AgentUsageResponse) -> Self {
        let usage_percentage = match (u.usage, u.limit) {
            (Some(used), Some(limit)) if limit > 0.0 => (used / limit) * 100.0,
            _ => 0.0,
        };

        let cost_used = u.usage.unwrap_or(0.0);
        let cost_limit = u.limit.unwrap_or(0.0);

        Self {
            provider_id: u.provider_id,
            provider_name: u.provider_name,
            usage_percentage,
            cost_used,
            cost_limit,
            payment_type: aic_core::PaymentType::UsageBased,
            usage_unit: u.usage_unit,
            is_quota_based: u.is_quota_based,
            is_available: u.is_available,
            ..Default::default()
        }
    }
}

#[tokio::main]
async fn main() {
    let cli = Cli::parse();
    
    // Initialize logging based on --debug flag
    tracing_subscriber::fmt()
        .with_max_level(if cli.debug { tracing::Level::DEBUG } else { tracing::Level::INFO })
        .init();
    
    let agent_url = cli.agent_url.trim_end_matches('/');
    let command = cli.command.unwrap_or_else(|| {
        print_usage();
        std::process::exit(0);
    });

    match command {
        Commands::Status => {
            show_status(agent_url, cli.all, cli.json, cli.debug).await;
        }
        Commands::List => {
            show_list(agent_url, cli.json).await;
        }
        Commands::Auth { provider } => {
            handle_auth(&provider).await;
        }
        Commands::Logout { provider } => {
            handle_logout(&provider).await;
        }
        Commands::Refresh => {
            refresh_usage(agent_url, cli.json).await;
        }
        Commands::History { provider_id, limit } => {
            show_history(agent_url, provider_id, limit, cli.json).await;
        }
        Commands::Health => {
            show_health(agent_url).await;
        }
    }
}

fn print_usage() {
    println!("Usage: aic-cli <command> [options]");
    println!();
    println!("Commands:");
    println!("  status    Show usage status");
    println!("  list      List configured providers");
    println!("  auth      Authenticate with a provider");
    println!("  logout    Logout from a provider");
    println!("  refresh   Refresh provider usage");
    println!("  history   Show historical usage");
    println!("  health    Show agent health status");
}

async fn show_status(
    agent_url: &str,
    show_all: bool,
    json: bool,
    debug: bool,
) {
    let client = reqwest::Client::new();
    let url = format!("{}/api/providers/usage", agent_url);

    let response = match client.get(&url).send().await {
        Ok(r) => r,
        Err(e) => {
            eprintln!("Failed to connect to agent at {}: {}", agent_url, e);
            eprintln!("Make sure agent service is running.");
            std::process::exit(1);
        }
    };

    let usages: Vec<AgentUsageResponse> = match response.json().await {
        Ok(u) => u,
        Err(e) => {
            eprintln!("Failed to parse response: {}", e);
            std::process::exit(1);
        }
    };

    let provider_usages: Vec<ProviderUsage> = usages.into_iter().map(ProviderUsage::from).collect();

    let filtered_usage: Vec<_> = if show_all {
        provider_usages
    } else {
        provider_usages.into_iter().filter(|u| u.is_available).collect()
    };

    if json {
        match serde_json::to_string_pretty(&filtered_usage) {
            Ok(json_str) => println!("{}", json_str),
            Err(e) => eprintln!("Error serializing to JSON: {}", e),
        }
    } else {
        let mut sorted: Vec<_> = filtered_usage.into_iter().collect();
        sorted.sort_by(|a, b| {
            a.provider_name
                .to_lowercase()
                .cmp(&b.provider_name.to_lowercase())
        });

        println!(
            "{:<36} | {:<14} | {:<10} | {}",
            "Provider", "Type", "Used", "Description"
        );
        println!("{}", "-".repeat(98));

        if sorted.is_empty() {
            println!("No active providers found.");
        }

        for u in sorted {
            let pct = if u.is_available {
                format!("{:.0}%", u.usage_percentage)
            } else {
                "-".to_string()
            };
            let type_str = if u.is_quota_based {
                "Quota"
            } else {
                "Pay-As-You-Go"
            };
            let account_info = if u.account_name.is_empty() {
                String::new()
            } else {
                format!(" [{}]", u.account_name)
            };

            let description = if u.description.is_empty() {
                account_info.trim().to_string()
            } else {
                format!("{}{}", u.description, account_info)
            };

            let lines: Vec<&str> = description.lines().collect();

            if lines.is_empty() {
                println!(
                    "{:<36} | {:<14} | {:<10} | {}",
                    u.provider_name, type_str, pct, ""
                );
            } else {
                println!(
                    "{:<36} | {:<14} | {:<10} | {}",
                    u.provider_name, type_str, pct, lines[0]
                );

                for line in &lines[1..] {
                    println!("{:<36} | {:<14} | {:<10} | {}", "", "", "", line);
                }
            }

            if debug {
                println!(
                    "{:<36} | {:<14} | {:<10} |   Unit: {}",
                    "", "", "", u.usage_unit
                );
                if let Some(reset_time) = u.next_reset_time {
                    println!(
                        "{:<36} | {:<14} | {:<10} |   Reset: {}",
                        "", "", "", reset_time
                    );
                }
                println!(
                    "{:<36} | {:<14} | {:<10} |   Auth: {}",
                    "", "", "", u.auth_source
                );
                if u.cost_limit > 0.0 {
                    println!(
                        "{:<36} | {:<14} | {:<10} |   Cost: {}/{}",
                        "", "", "", u.cost_used, u.cost_limit
                    );
                }
            }

            if let Some(details) = &u.details {
                for d in details {
                    let name = format!("  {}", d.name);
                    println!(
                        "{:<36} | {:<14} | {:<10} | {}",
                        name, "", d.used, d.description
                    );
                }
            }
        }
    }
}

async fn show_list(
    _agent_url: &str,
    json: bool,
) {
    let client = reqwest::Client::new();
    let agent_url = format!("{}/api/providers/discovered", _agent_url);

    let mut configs: Vec<aic_core::ProviderConfig> = Vec::new();
    let mut agent_available = false;

    match client.get(&agent_url).send().await {
        Ok(response) => {
            match response.json::<Vec<aic_core::ProviderConfig>>().await {
                Ok(providers) => {
                    configs = providers;
                    agent_available = true;
                }
                Err(e) => {
                    eprintln!("Failed to parse providers from agent: {}", e);
                }
            }
        }
        Err(e) => {
            eprintln!("Failed to connect to agent: {}", e);
        }
    };

    // Fallback to local discovery if agent is not available or returned no configs
    if configs.is_empty() {
        let config_loader = ConfigLoader::new(client);
        configs = config_loader.load_config().await;
    }

    if json {
        match serde_json::to_string_pretty(&configs) {
            Ok(json_str) => println!("{}", json_str),
            Err(e) => eprintln!("Error serializing to JSON: {}", e),
        }
    } else {
        for c in configs {
            println!("ID: {}, Type: {}", c.provider_id, c.config_type);
        }
    }
}

async fn refresh_usage(
    agent_url: &str,
    json: bool,
) {
    println!("Triggering usage refresh...");

    let client = reqwest::Client::new();
    let url = format!("{}/api/providers/usage/refresh", agent_url);

    let response = match client.post(&url).send().await {
        Ok(r) => r,
        Err(e) => {
            eprintln!("Failed to connect to agent: {}", e);
            std::process::exit(1);
        }
    };

    let usages: Vec<AgentUsageResponse> = match response.json().await {
        Ok(u) => u,
        Err(e) => {
            eprintln!("Failed to parse response: {}", e);
            std::process::exit(1);
        }
    };

    println!("✓ Usage refreshed successfully. Updated {} providers.", usages.len());

    if json {
        match serde_json::to_string_pretty(&usages) {
            Ok(json_str) => println!("{}", json_str),
            Err(e) => eprintln!("Error serializing to JSON: {}", e),
        }
    }
}

async fn show_history(
    agent_url: &str,
    provider_id: Option<String>,
    limit: usize,
    json: bool,
) {
    let mut url = format!("{}/api/history?limit={}", agent_url, limit);

    if let Some(provider) = provider_id {
        url.push_str(&format!("&provider_id={}", provider));
    }

    let client = reqwest::Client::new();
    let response = match client.get(&url).send().await {
        Ok(r) => r,
        Err(e) => {
            eprintln!("Failed to connect to agent: {}", e);
            std::process::exit(1);
        }
    };

    let history: HistoricalUsageResponse = match response.json().await {
        Ok(h) => h,
        Err(e) => {
            eprintln!("Failed to parse response: {}", e);
            std::process::exit(1);
        }
    };

    if json {
        match serde_json::to_string_pretty(&history) {
            Ok(json_str) => println!("{}", json_str),
            Err(e) => eprintln!("Error serializing to JSON: {}", e),
        }
    } else {
        println!("Historical Usage ({} records):", history.total_records);
        println!();

        for record in &history.records {
            println!(
                "{} | {} | {:.2} / {} {} | {}",
                record.timestamp,
                record.provider_name,
                record.usage,
                record.limit.map(|l| l.to_string()).unwrap_or_else(|| "-".to_string()),
                record.usage_unit,
                if record.is_quota_based { "Quota" } else { "Pay-As-You-Go" }
            );
        }
    }
}

async fn show_health(agent_url: &str) {
    let url = format!("{}/health", agent_url);

    let client = reqwest::Client::new();
    let response = match client.get(&url).send().await {
        Ok(r) => r,
        Err(e) => {
            eprintln!("Failed to connect to agent: {}", e);
            std::process::exit(1);
        }
    };

    #[derive(Deserialize)]
    struct HealthResponse {
        status: String,
        version: String,
        uptime_seconds: u64,
    }

    let health: HealthResponse = match response.json().await {
        Ok(h) => h,
        Err(e) => {
            eprintln!("Failed to parse response: {}", e);
            std::process::exit(1);
        }
    };

    println!("Agent Status:");
    println!("  Status: {}", health.status);
    println!("  Version: {}", health.version);
    println!("  Uptime: {}s", health.uptime_seconds);
}

async fn handle_auth(provider: &str) {
    if provider.to_lowercase() != "github" {
        println!("Unknown provider for auth: {}", provider);
        println!("Supported providers: github");
        return;
    }

    let client = reqwest::Client::new();
    let auth_service = std::sync::Arc::new(GitHubAuthService::new(client.clone()));
    let config_loader = std::sync::Arc::new(ConfigLoader::new(client));
    let auth_manager = AuthenticationManager::new(auth_service.clone(), config_loader.clone());

    auth_manager.initialize_from_config().await;

    if auth_manager.is_authenticated() {
        println!("Already authenticated with GitHub.");
        print!("Would you like to re-authenticate? [y/N]: ");
        let _ = io::stdout().flush();
        let mut input = String::new();
        if io::stdin().read_line(&mut input).is_ok() {
            if !input.trim().eq_ignore_ascii_case("y") {
                println!("Authentication cancelled.");
                return;
            }
        }
    }

    println!("Initiating GitHub Device Flow...\n");

    match auth_manager.initiate_login().await {
        Ok(device_flow) => {
            println!("Please visit: {}", device_flow.verification_uri);
            println!("Enter the following code: {}\n", device_flow.user_code);

            open_browser(&device_flow.verification_uri);

            println!("Waiting for authentication...");

            match auth_manager
                .wait_for_login(&device_flow.device_code, device_flow.interval as u64)
                .await
            {
                Ok(true) => {
                    println!("\n✓ Successfully authenticated with GitHub!");
                    println!("GitHub Copilot provider is now active.");
                }
                Ok(false) => {
                    println!("\n✗ Authentication failed or was cancelled.");
                    std::process::exit(1);
                }
                Err(e) => {
                    println!("\n✗ Authentication error: {}", e);
                    std::process::exit(1);
                }
            }
        }
        Err(e) => {
            eprintln!("Failed to initiate device flow: {}", e);
            std::process::exit(1);
        }
    }
}

async fn handle_logout(provider: &str) {
    if provider.to_lowercase() != "github" {
        println!("Unknown provider for logout: {}", provider);
        println!("Supported providers: github");
        return;
    }

    let client = reqwest::Client::new();
    let auth_service = std::sync::Arc::new(GitHubAuthService::new(client.clone()));
    let config_loader = std::sync::Arc::new(ConfigLoader::new(client));
    let auth_manager = AuthenticationManager::new(auth_service.clone(), config_loader.clone());

    auth_manager.initialize_from_config().await;

    if !auth_manager.is_authenticated() {
        println!("Not currently authenticated with GitHub.");
        return;
    }

    match auth_manager.logout().await {
        Ok(_) => {
            println!("✓ Successfully logged out from GitHub.");
        }
        Err(e) => {
            eprintln!("✗ Failed to logout: {}", e);
            std::process::exit(1);
        }
    }
}

fn open_browser(url: &str) {
    #[cfg(target_os = "windows")]
    {
        let _ = Command::new("cmd").args(["/C", "start", url]).spawn();
    }
    #[cfg(target_os = "macos")]
    {
        let _ = Command::new("open").arg(url).spawn();
    }
    #[cfg(target_os = "linux")]
    {
        let _ = Command::new("xdg-open").arg(url).spawn();
    }
}
