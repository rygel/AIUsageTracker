# AI Consumption Tracker - User Manual

Welcome to the **AI Consumption Tracker** user manual. This guide will help you understand the features of the graphical user interface and how to manage your AI provider consumption effectively.

## Dashboard Overview

The main dashboard provides a real-time overview of your AI usage across various providers. It is designed to be lightweight, stay out of your way, and provide at-a-glance information.

### Top Bar & Window Controls
- **Draggable Header**: You can move the window by clicking and dragging the top dark bar.
- **Close (X)**: Closes the dashboard window. The application continues running in the system tray if configured.

---

## User Interface Features

The footer of the dashboard contains several toggles and buttons to customize your experience:

### 1. Show All
- **Toggle**: Filters the list of providers.
- **Behavior**: When enabled, all configured providers are shown. When disabled, only providers with active usage or specific alerts are displayed, keeping your list clean and focused.

### 2. Top (Always on Top)
- **Toggle**: Controls window layering.
- **Behavior**: When checked, the dashboard will stay above all other windows, ensuring your consumption data is always visible while you work.

### 3. Pin (Stay Open)
- **Toggle**: Controls auto-hide behavior.
- **Behavior**: 
    - **Pinned**: The window remains open until you manually close it.
    - **Unpinned**: The window will automatically hide when it loses focus (e.g., when you click into another application), making it perfect for quick checks.

### 4. Compact View
- **Toggle**: Adjusts the layout density.
- **Behavior**: When enabled, the UI uses a more condensed layout with smaller fonts and tighter spacing, ideal for keeping the window small on your screen.

### 5. Refresh (🔄 Icon)
- **Action**: Triggers an immediate update.
- **Behavior**: Fetches the latest usage data from all configured API providers.
- **Auto Refresh**: The app also performs periodic background refreshes. You can configure the interval (in minutes) in the **Layout** tab of the Settings window. Setting this to **0** disables automatic background refreshing.

### 6. Settings (⚙️ Icon)
- **Action**: Opens the **Provider Settings** window.
- **Features**:
    - **API Keys**: Configure your keys for OpenAI, Anthropic, Gemini, etc.
    - **Layout Tab**:
        - **Auto Refresh (Minutes)**: Configure how often the app refreshes in the background (Default: 5).
        - **Privacy Mode**: Toggle to mask sensitive information like account names and specific token counts.
        - **Scan for Keys**: Automatically searches your environment variables and files for existing AI API keys to speed up setup.
        - **Save/Cancel**: Apply your changes or discard them without saving.
        - **Recent Changes (v1.7.4)**:
            - Privacy mode is now only accessible via the dashboard footer button (Settings dialog duplicate removed)
            - Fixed bug where privacy toggle didn't update the UI display
            - Enhanced code quality with `.editorconfig` and Roslyn analyzer rules

![Settings UI](../docs/screenshot_settings_privacy.png)

### 7. Info Dialog
- **Action**: Accessible via the Settings menu or right-click.
- **Content**: Displays version information, credits, and links to the project repository.

![Info Dialog](../docs/screenshot_info_privacy.png)

### 8. Tray Context Menu
- **Access**: Right-click the application icon in the system tray.
- **Features**: Quick access to the Dashboard, Settings, Info, and Exit.

![Tray Menu](../docs/screenshot_context_menu_privacy.png)

### 9. Tray Status Icons
The application uses dynamic tray icons to show usage levels at a glance:
- **Green**: Low usage
- **Yellow**: Medium usage (approaching threshold)
- **Red**: High usage (exceeding threshold)
 
![Green](../docs/tray_icon_good.png) ![Yellow](../docs/tray_icon_warning.png) ![Red](../docs/tray_icon_danger.png)

### 6. Antigravity Offline Support
- **Caching**: The application now caches Antigravity usage data when the application is running successfully.
- **Offline Display**: When Antigravity is not running, the tracker shows cached usage data with a "Last refreshed: Xm ago" countdown timer.
- **Reset Information**: If reset times are available in cached data, the tracker displays "Resets in Xh Ym" to show when quota will be refilled.
- **Behavior**: This ensures you can monitor Antigravity usage even when the application is closed, with clear indication of data staleness and upcoming quota resets.

### 10. Invert Progress Bars (Health Bar Mode)
- **Setting**: Found in the main dashboard or settings.
- **Behavior**: 
    - **Enabled (Default)**: Bars represent **Remaining** capacity (Start Full/Green -> End Empty/Red).
    - **Disabled**: Bars represent **Used** capacity (Start Empty -> End Full/Red).
- **Logic**: Colors are standardized based on usage level (Red for >80% usage) regardless of the display mode.

### 6. Antigravity Offline Support
- **Caching**: The application now caches Antigravity usage data when the application is running successfully.
- **Offline Display**: When Antigravity is not running, the tracker shows cached usage data with a "Last refreshed: Xm ago" countdown timer.
- **Reset Information**: If reset times are available in cached data, the tracker displays "Resets in Xh Ym" to show when quota will be refilled.
- **Behavior**: This ensures you can monitor Antigravity usage even when the application is closed, with clear indication of data staleness and upcoming quota resets.

### 10. Invert Progress Bars (Health Bar Mode)
- **Setting**: Found in the main dashboard or settings.

> **Note**: For detailed information on setting up environment variables for automatic discovery, see [Environment Variables Guide](environment_variables.md).

**OpenAI Users**: You can also authenticate via JWT tokens by running `opencode-tracker auth openai` in the OpenCode CLI. This provides actual usage data and credit balance from the ChatGPT backend API.

---

## System Tray Integration
The application runs primarily in the system tray (near the clock). You can right-click the icon to:
- Open the Dashboard.
- Open Settings directly.
- Exit the application completely.

---

## Environment Variables

AI Consumption Tracker can automatically discover API keys from environment variables. This allows for seamless integration with your existing development environment without manually entering keys in the UI.

### Supported Environment Variables

| Environment Variable | Provider | Notes |
|:---------------------|:---------|:------|
| **ANTHROPIC_API_KEY** | Anthropic (Claude) | Primary key for Claude Code integration |
| **CLAUDE_API_KEY** | Anthropic (Claude) | Alternative name for Claude authentication |
| **OPENAI_API_KEY** | OpenAI | Used for OpenAI/Codex integration |
| **MINIMAX_API_KEY** | Minimax | For both China and International variants |
| **KIMI_API_KEY** | Kimi (Moonshot) | Primary key for Moonshot AI |
| **MOONSHOT_API_KEY** | Kimi (Moonshot) | Alternative name for Moonshot |
| **XIAOMI_API_KEY** | Xiaomi | Primary key for Xiaomi integration |
| **MIMO_API_KEY** | Xiaomi | Alternative name for Xiaomi |

### How It Works

1. **Automatic Discovery**: When you click "Scan for Keys" in the Settings dialog, the application searches for these environment variables
2. **Auto-Population**: Found keys are automatically added to the appropriate provider configuration
3. **Source Tracking**: The application tracks where each key was discovered (shown as "Env: VARIABLE_NAME" in the Auth Source column)

### Setting Environment Variables

#### Windows (PowerShell)
```powershell
[Environment]::SetEnvironmentVariable("ANTHROPIC_API_KEY", "your-key-here", "User")
```

#### Windows (Command Prompt)
```cmd
setx ANTHROPIC_API_KEY "your-key-here"
```

#### Linux/macOS (Bash)
```bash
export ANTHROPIC_API_KEY="your-key-here"
```

**Note**: After setting environment variables, you may need to restart the application or your terminal for changes to take effect.

### Security Considerations

- Environment variables are stored in your system's user profile
- The application reads but never writes to environment variables
- Keys discovered via environment variables are cached in the application's secure storage for offline use
- Consider using a secrets manager or `.env` file loader for team environments
