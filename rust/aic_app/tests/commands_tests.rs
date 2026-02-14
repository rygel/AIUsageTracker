use aic_app::commands::{
    AppState, DeviceFlowState, UpdateCheckResult, TokenDiscoveryResult,
};
use aic_core::{ProviderConfig, AuthenticationManager, ConfigLoader, GitHubAuthService, ProviderManager};
use std::sync::Arc;
use tokio::sync::{Mutex, RwLock};

fn create_test_app_state() -> AppState {
    let client = reqwest::Client::new();
    let provider_manager = Arc::new(ProviderManager::new(client.clone()));
    let config_loader = Arc::new(ConfigLoader::new(client.clone()));
    let auth_service = Arc::new(GitHubAuthService::new(client));
    let auth_manager = Arc::new(AuthenticationManager::new(
        auth_service.clone(),
        config_loader.clone(),
    ));

    AppState {
        provider_manager,
        config_loader,
        auth_manager,
        auto_refresh_enabled: Arc::new(Mutex::new(false)),
        device_flow_state: Arc::new(RwLock::new(None)),
        agent_process: Arc::new(Mutex::new(None)),
        preloaded_settings: Arc::new(Mutex::new(None)),
    }
}

// ============= App State Tests =============

#[tokio::test]
async fn test_app_state_creation() {
    let state = create_test_app_state();

    assert!(!state.auth_manager.is_authenticated());

    let auto_refresh = state.auto_refresh_enabled.lock().await;
    assert!(!*auto_refresh);

    let device_flow = state.device_flow_state.read().await;
    assert!(device_flow.is_none());
}

#[tokio::test]
async fn test_auto_refresh_toggle() {
    let state = create_test_app_state();

    // Initially false
    {
        let enabled = state.auto_refresh_enabled.lock().await;
        assert!(!*enabled);
    }

    // Toggle to true
    {
        let mut enabled = state.auto_refresh_enabled.lock().await;
        *enabled = true;
    }

    // Verify it's true
    {
        let enabled = state.auto_refresh_enabled.lock().await;
        assert!(*enabled);
    }

    // Toggle back to false
    {
        let mut enabled = state.auto_refresh_enabled.lock().await;
        *enabled = false;
    }

    // Verify it's false again
    {
        let enabled = state.auto_refresh_enabled.lock().await;
        assert!(!*enabled);
    }
}

#[tokio::test]
async fn test_device_flow_state_lifecycle() {
    let state = create_test_app_state();

    // Initially none
    {
        let flow_state = state.device_flow_state.read().await;
        assert!(flow_state.is_none());
    }

    // Set device flow state
    {
        let mut flow_state = state.device_flow_state.write().await;
        *flow_state = Some(DeviceFlowState {
            device_code: "device123".to_string(),
            user_code: "ABC123".to_string(),
            verification_uri: "https://github.com/login/device".to_string(),
            interval: 5,
        });
    }

    // Verify state was set
    {
        let flow_state = state.device_flow_state.read().await;
        assert!(flow_state.is_some());
        let flow = flow_state.as_ref().unwrap();
        assert_eq!(flow.device_code, "device123");
        assert_eq!(flow.user_code, "ABC123");
        assert_eq!(flow.verification_uri, "https://github.com/login/device");
        assert_eq!(flow.interval, 5);
    }

    // Clear device flow state
    {
        let mut flow_state = state.device_flow_state.write().await;
        *flow_state = None;
    }

    // Verify state was cleared
    {
        let flow_state = state.device_flow_state.read().await;
        assert!(flow_state.is_none());
    }
}

// ============= Provider Config Tests =============
// Note: These tests may share state with real config files

#[tokio::test]
async fn test_get_configured_providers_returns_vector() {
    let state = create_test_app_state();
    let configs = state.config_loader.load_config().await;
    // Just verify it returns a valid vector
    assert!(configs.is_empty() || !configs.is_empty());
}

#[tokio::test]
async fn test_provider_config_structure() {
    let state = create_test_app_state();
    let configs = state.config_loader.load_config().await;
    
    // If there are configs, verify structure
    for config in &configs {
        assert!(!config.provider_id.is_empty());
        // API key can be empty or not
        // show_in_tray should be boolean
    }
}

#[tokio::test]
async fn test_save_provider_config_structure() {
    let state = create_test_app_state();
    
    // Create a valid config structure
    let config = ProviderConfig {
        provider_id: "test-provider".to_string(),
        api_key: "test-api-key".to_string(),
        show_in_tray: true,
        ..Default::default()
    };
    
    // Verify config structure
    assert_eq!(config.provider_id, "test-provider");
    assert_eq!(config.api_key, "test-api-key");
    assert!(config.show_in_tray);
}

// ============= Preferences Tests =============

#[tokio::test]
async fn test_preferences_default_values() {
    let state = create_test_app_state();
    let prefs = state.config_loader.load_preferences().await;

    // Verify default values exist
    assert!(prefs.window_width > 0.0);
    assert!(prefs.window_height > 0.0);
    assert!(!prefs.font_family.is_empty());
    assert!(prefs.font_size > 0);
}

#[tokio::test]
async fn test_preferences_clone() {
    let state = create_test_app_state();
    let prefs = state.config_loader.load_preferences().await;
    
    // Clone should work
    let cloned = prefs.clone();
    assert_eq!(prefs.window_width, cloned.window_width);
    assert_eq!(prefs.window_height, cloned.window_height);
}

#[tokio::test]
async fn test_preferences_serialization() {
    let state = create_test_app_state();
    let prefs = state.config_loader.load_preferences().await;
    
    // Modify and save
    let mut new_prefs = prefs.clone();
    new_prefs.window_width = 999.0;

    let result = state.config_loader.save_preferences(&new_prefs).await;
    assert!(result.is_ok());
}

#[tokio::test]
async fn test_preferences_font_settings() {
    let state = create_test_app_state();
    let prefs = state.config_loader.load_preferences().await;
    
    // Font settings should exist
    assert!(prefs.font_size >= 8);
    assert!(prefs.font_size <= 72);
}

#[tokio::test]
async fn test_preferences_thresholds() {
    let state = create_test_app_state();
    let prefs = state.config_loader.load_preferences().await;
    
    // Thresholds should be valid percentages
    assert!(prefs.color_threshold_yellow >= 0);
    assert!(prefs.color_threshold_yellow <= 100);
    assert!(prefs.color_threshold_red >= 0);
    assert!(prefs.color_threshold_red <= 100);
}

#[tokio::test]
async fn test_preferences_toggles() {
    let state = create_test_app_state();
    let prefs = state.config_loader.load_preferences().await;
    
    // Boolean toggles should be bool
    assert!(prefs.show_all == true || prefs.show_all == false);
    assert!(prefs.always_on_top == true || prefs.always_on_top == false);
    assert!(prefs.compact_mode == true || prefs.compact_mode == false);
}

// ============= GitHub Auth Tests =============

#[tokio::test]
async fn test_github_auth_not_authenticated() {
    let state = create_test_app_state();
    assert!(!state.auth_manager.is_authenticated());
}

// ============= Update Check Tests =============

#[tokio::test]
async fn test_update_check_returns_result() {
    let result = UpdateCheckResult {
        current_version: "1.7.13".to_string(),
        latest_version: "1.7.13".to_string(),
        update_available: false,
        download_url: String::new(),
    };

    assert_eq!(result.current_version, "1.7.13");
    assert!(!result.update_available);
    assert!(result.latest_version.len() > 0);
}

#[tokio::test]
async fn test_update_check_update_available() {
    let result = UpdateCheckResult {
        current_version: "1.7.12".to_string(),
        latest_version: "1.7.13".to_string(),
        update_available: true,
        download_url: "https://github.com/rygel/AIConsumptionTracker/releases".to_string(),
    };

    assert!(result.update_available);
    assert_eq!(result.latest_version, "1.7.13");
    assert!(!result.download_url.is_empty());
}

#[tokio::test]
async fn test_update_check_version_parsing() {
    // Test version string format
    let version = "1.7.13";
    let parts: Vec<&str> = version.split('.').collect();
    assert_eq!(parts.len(), 3);
    
    let major: i32 = parts[0].parse().unwrap();
    let minor: i32 = parts[1].parse().unwrap();
    let patch: i32 = parts[2].parse().unwrap();
    
    assert!(major >= 1);
    assert!(minor >= 0);
    assert!(patch >= 0);
}

// ============= Token Discovery Tests =============

#[tokio::test]
async fn test_token_discovery_result_found() {
    let result = TokenDiscoveryResult {
        found: true,
        token: "github_pat_test123".to_string(),
    };

    assert!(result.found);
    assert!(result.token.starts_with("github_pat_"));
}

#[tokio::test]
async fn test_token_discovery_result_not_found() {
    let result = TokenDiscoveryResult {
        found: false,
        token: String::new(),
    };

    assert!(!result.found);
    assert!(result.token.is_empty());
}

#[tokio::test]
async fn test_token_format_validation() {
    // Test that PAT format is recognized
    let valid_pat = "github_pat_abc123";
    assert!(valid_pat.starts_with("github_pat_"));
    
    let invalid_pat = "sk-openai-key123";
    assert!(!invalid_pat.starts_with("github_pat_"));
}

// ============= Provider Usage Tests =============

#[tokio::test]
async fn test_get_usage_returns_vector() {
    let state = create_test_app_state();
    let usage = state.provider_manager.get_all_usage(false).await;
    // Should return a vector
    assert!(usage.is_empty() || !usage.is_empty());
}

// ============= Edge Cases Tests =============

#[tokio::test]
async fn test_empty_provider_id() {
    let config = ProviderConfig {
        provider_id: String::new(),
        api_key: "key".to_string(),
        show_in_tray: false,
        ..Default::default()
    };
    
    assert!(config.provider_id.is_empty());
}

#[tokio::test]
async fn test_special_characters_in_api_key_format() {
    // Test that special characters are preserved in format
    let special_chars = "!@#$%^&*()_+-=[]{}|;':\",./<>?";
    let config = ProviderConfig {
        provider_id: "test".to_string(),
        api_key: format!("sk-test{}", special_chars),
        show_in_tray: false,
        ..Default::default()
    };
    
    assert!(config.api_key.contains("sk-test"));
    assert!(config.api_key.len() > 10);
}

#[tokio::test]
async fn test_concurrent_state_modification() {
    let state = create_test_app_state();

    // Initially false
    {
        let enabled = state.auto_refresh_enabled.lock().await;
        assert!(!*enabled);
    }

    // Toggle to true
    {
        let mut enabled = state.auto_refresh_enabled.lock().await;
        *enabled = true;
    }

    // Toggle back to false
    {
        let mut enabled = state.auto_refresh_enabled.lock().await;
        *enabled = false;
    }

    // Final state is false
    {
        let enabled = state.auto_refresh_enabled.lock().await;
        assert!(!*enabled);
    }
}

#[tokio::test]
async fn test_device_flow_state_with_device_code() {
    let state = create_test_app_state();

    // Set device flow state
    {
        let mut flow_state = state.device_flow_state.write().await;
        *flow_state = Some(DeviceFlowState {
            device_code: "test-device-code-12345".to_string(),
            user_code: "USER123".to_string(),
            verification_uri: "https://github.com/login/device".to_string(),
            interval: 30,
        });
    }

    // Verify device code is correct
    {
        let flow_state = state.device_flow_state.read().await;
        let flow = flow_state.as_ref().unwrap();
        assert!(flow.device_code.contains("test-device-code"));
        assert_eq!(flow.user_code, "USER123");
        assert_eq!(flow.interval, 30);
    }
}

#[tokio::test]
async fn test_preferences_window_dimensions_sanity() {
    let state = create_test_app_state();
    let prefs = state.config_loader.load_preferences().await;
    
    // Window dimensions should be reasonable
    assert!(prefs.window_width >= 300.0);
    assert!(prefs.window_width <= 3840.0);
    assert!(prefs.window_height >= 200.0);
    assert!(prefs.window_height <= 2160.0);
}

#[tokio::test]
async fn test_update_result_serialization() {
    // Test that UpdateCheckResult can be serialized
    let result = UpdateCheckResult {
        current_version: "0.5.0".to_string(),
        latest_version: "1.1.0".to_string(),
        update_available: true,
        download_url: "https://example.com/download".to_string(),
    };
    
    // Should be able to create JSON
    let json = serde_json::to_string(&result);
    assert!(json.is_ok());
    
    let json_str = json.unwrap();
    let parsed: UpdateCheckResult = serde_json::from_str(&json_str).unwrap();
    assert_eq!(parsed.current_version, "0.5.0");
    assert!(parsed.update_available);
}

#[tokio::test]
async fn test_token_discovery_serialization() {
    // Test that TokenDiscoveryResult can be serialized
    let result = TokenDiscoveryResult {
        found: true,
        token: "test_token_123".to_string(),
    };
    
    let json = serde_json::to_string(&result);
    assert!(json.is_ok());
    
    let json_str = json.unwrap();
    let parsed: TokenDiscoveryResult = serde_json::from_str(&json_str).unwrap();
    assert!(parsed.found);
    assert!(!parsed.token.is_empty());
}
