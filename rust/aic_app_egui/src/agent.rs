use std::process::Child;
use std::time::Duration;

pub struct AgentManager {
    process: Option<Child>,
    pub is_starting: bool,
    pub last_error: Option<String>,
}

impl Default for AgentManager {
    fn default() -> Self {
        Self {
            process: None,
            is_starting: false,
            last_error: None,
        }
    }
}

impl AgentManager {
    pub fn new() -> Self {
        Self::default()
    }

    pub fn find_agent_executable() -> Result<String, String> {
        let exe_name = if cfg!(target_os = "windows") {
            "aic_agent.exe"
        } else {
            "aic_agent"
        };

        let mut possible_paths: Vec<std::path::PathBuf> = Vec::new();

        if let Ok(current_dir) = std::env::current_dir() {
            possible_paths.push(current_dir.join(exe_name));
            possible_paths.push(current_dir.join("target").join("debug").join(exe_name));
            possible_paths.push(current_dir.join("target").join("release").join(exe_name));
            possible_paths.push(current_dir.join("..").join("target").join("debug").join(exe_name));
            possible_paths.push(current_dir.join("..").join("target").join("release").join(exe_name));
            possible_paths.push(current_dir.join("..").join("..").join("target").join("debug").join(exe_name));
            possible_paths.push(current_dir.join("..").join("..").join("target").join("release").join(exe_name));
        }

        possible_paths.push(std::path::PathBuf::from(format!("./{}", exe_name)));
        possible_paths.push(std::path::PathBuf::from(format!("../target/debug/{}", exe_name)));
        possible_paths.push(std::path::PathBuf::from(format!("../target/release/{}", exe_name)));
        possible_paths.push(std::path::PathBuf::from(format!("../../target/debug/{}", exe_name)));
        possible_paths.push(std::path::PathBuf::from(format!("../../target/release/{}", exe_name)));

        for path in possible_paths.iter() {
            if path.exists() {
                log::info!("Found agent executable at: {:?}", path);
                return Ok(path.to_string_lossy().to_string());
            }
        }

        let cwd = std::env::current_dir()
            .map(|p| p.to_string_lossy().to_string())
            .unwrap_or_else(|_| "unknown".to_string());
        Err(format!(
            "Agent executable '{}' not found. Current directory: {}. Build with: cargo build -p aic_agent",
            exe_name, cwd
        ))
    }

    pub fn check_and_cleanup(&mut self) {
        if let Some(ref mut child) = self.process {
            match child.try_wait() {
                Ok(None) => {}
                Ok(exit_code) => {
                    log::info!("Agent process exited with code: {:?}", exit_code);
                    self.process = None;
                }
                Err(e) => {
                    log::warn!("Failed to check agent process: {}", e);
                    self.process = None;
                }
            }
        }
    }

    pub fn is_process_running(&mut self) -> bool {
        if let Some(ref mut child) = self.process {
            match child.try_wait() {
                Ok(None) => true,
                Ok(_) => {
                    self.process = None;
                    false
                }
                Err(_) => {
                    self.process = None;
                    false
                }
            }
        } else {
            false
        }
    }

    pub fn start(&mut self) -> Result<bool, String> {
        if self.is_starting {
            return Ok(false);
        }

        self.check_and_cleanup();

        if self.is_process_running() {
            log::info!("Agent process already running");
            return Ok(true);
        }

        self.is_starting = true;
        self.last_error = None;

        let agent_path = Self::find_agent_executable()?;

        log::info!("Starting agent from: {}", agent_path);

        match std::process::Command::new(&agent_path).spawn() {
            Ok(child) => {
                let pid = child.id();
                self.process = Some(child);
                log::info!("Agent started with PID: {}", pid);
                self.is_starting = false;
                Ok(true)
            }
            Err(e) => {
                let error_msg = format!("Failed to start agent: {}", e);
                log::error!("{}", error_msg);
                self.last_error = Some(error_msg.clone());
                self.is_starting = false;
                Err(error_msg)
            }
        }
    }

    pub fn kill(&mut self) {
        if let Some(ref mut child) = self.process {
            let _ = child.kill();
            log::info!("Agent process killed");
        }
        self.process = None;
    }
}

pub async fn wait_for_agent_ready(client: &crate::http_client::AgentClient, timeout_secs: u64) -> bool {
    let start = std::time::Instant::now();
    let timeout = Duration::from_secs(timeout_secs);

    while start.elapsed() < timeout {
        match client.check_agent_status().await {
            Ok(status) if status.is_running => {
                log::info!("Agent ready after {:?}", start.elapsed());
                return true;
            }
            _ => {
                tokio::time::sleep(Duration::from_millis(200)).await;
            }
        }
    }

    log::warn!("Agent not ready after {:?}", timeout);
    false
}
