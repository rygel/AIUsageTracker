# Data Flow Documentation

This document describes how the AI Consumption Tracker reads and writes data, including the specific files and the order in which they are processed.

## Configuration & Authentication (Read)

The application attempts to load provider configurations (API keys, base URLs, etc.) from multiple locations in the following order. The first occurrence of a provider configuration is used.

1.  **`%USERPROFILE%\.ai-consumption-tracker\auth.json`**: The primary configuration file for this application.
2.  **`%USERPROFILE%\.local\share\opencode\auth.json`**: Compatibility path for OpenCode.
3.  **`%APPDATA%\opencode\auth.json`**: Standard application data path.
4.  **`%LOCALAPPDATA%\opencode\auth.json`**: Local application data path.
5.  **`%USERPROFILE%\.opencode\auth.json`**: Legacy root path.

### Token Discovery (Read)

If a provider's API key is not found in the configuration files above, the application performs an automatic discovery in this order:

1.  **Environment Variables**:
    - `MINIMAX_API_KEY`
    - `XIAOMI_API_KEY` / `MIMO_API_KEY`
    - `KIMI_API_KEY` / `MOONSHOT_API_KEY`
    - `ANTHROPIC_API_KEY` / `CLAUDE_API_KEY`
    - `OPENAI_API_KEY`
2.  **Kilo Code Secrets**: `%USERPROFILE%\.kilocode\secrets.json`
3.  **Providers Definition**: `%USERPROFILE%\.local\share\opencode\providers.json`

## Application Preferences (Read)

User preferences (font settings, layout, tray options) are loaded in this order:

1.  **`auth.json` (app_settings key)**: Preferences are now unified into the primary `auth.json` file.
2.  **`%USERPROFILE%\.ai-consumption-tracker\preferences.json`**: Legacy fallback path.

## Data Persistence (Write)

All user changes made through the Settings UI are written to a single location:

1.  **`%USERPROFILE%\.ai-consumption-tracker\auth.json`**
    - **Provider Configs**: Individual provider keys and tray options are updated.
    - **App Settings**: The `app_settings` root key is updated with current `AppPreferences`.

> [!NOTE]
> The application preserves existing `app_settings` when saving provider configurations to ensure no data loss.
