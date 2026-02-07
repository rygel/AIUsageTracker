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

If a provider's API key is not found in configuration files above, application performs an automatic discovery in this order:

1.  **Environment Variables**:
    - `OPENAI_API_KEY` - OpenAI API key
    - `ANTHROPIC_API_KEY` or `CLAUDE_API_KEY` - Anthropic/Claude API key
    - `GEMINI_API_KEY` or `GOOGLE_API_KEY` - Google Gemini API key
    - `DEEPSEEK_API_KEY` - DeepSeek API key
    - `OPENROUTER_API_KEY` - OpenRouter API key
    - `KIMI_API_KEY` or `MOONSHOT_API_KEY` - Kimi API key
    - `XIAOMI_API_KEY` or `MIMO_API_KEY` - Xiaomi API key
    - `MINIMAX_API_KEY` - Minimax API key
    - `ZAI_API_KEY` or `Z_AI_API_KEY` - Z.AI API key
    - `ANTIGRAVITY_API_KEY` or `GOOGLE_ANTIGRAVITY_API_KEY` - Google Antigravity API key
    - `OPENCODE_API_KEY` - OpenCode API key
    - `OPENCODE_ZEN_API_KEY` - OpenCode Zen API key
    - `CLOUDCODE_API_KEY` - CloudCode API key
    - `CODEX_API_KEY` - Codex API key
2.  **Kilo Code Secrets**: `%USERPROFILE%\.kilocode\secrets.json`
    - Automatically discovers OpenAI keys configured in Roo Cline
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
