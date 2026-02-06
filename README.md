# <img src="AIConsumptionTracker.UI/Assets/app_icon.png" width="32" height="32" valign="middle"> AI Consumption Tracker

![Version](https://img.shields.io/badge/version-1.5.0-blue)
![License](https://img.shields.io/badge/license-MIT-green)
![Platforms](https://img.shields.io/badge/platforms-Windows%20|%20Linux%20-blue)
![Language](https://img.shields.io/badge/language-C%23%20|%20.NET-purple)
![Downloads](https://img.shields.io/github/downloads/rygel/AIConsumptionTracker/total)

A streamlined Windows dashboard and tray utility to monitor AI API usage, costs, and quotas across multiple providers.

<img width="544" height="643" alt="image" src="https://github.com/user-attachments/assets/d76b2f57-0d19-43d2-be6b-765f582d8dcc" />


### Documentation
- [User Manual](docs/user_manual.md)
- [CLI Reference](docs/cli_documentation.md)

## Download
Download the latest installer or .zip file from the [Release](https://github.com/rygel/AIConsumptionTracker/releases) page.

## Key Features

- **Multi-Provider Support**: Track usage for Anthropic, Gemini, OpenRouter, OpenCode, Kilo Code, DeepSeek, OpenAI, Google Cloud Code, GitHub Copilot, Codex, and more.
- **Smart Discovery**: Automatically scans environment variables and standard configuration files for existing API keys.
- **Minimalist Dashboard**: A compact, topmost window providing a quick overview of your current spend and token usage.
- **Dynamic Tray Integration**:
  - **Auto-Hide**: Dashboard hides automatically when focus is lost.
  - **Individual Tracking**: Option to spawn separate tray icons for specific providers.
  - **Live Progress Bars**: Tray icons feature "Core Temp" style bars that reflect usage levels in real-time.
- **Inverted Progress Bars**: Option to show "Remaining" capacity (starting green/full) instead of "Used" capacity (starting empty).
- **Secure Management**: Manage all keys and preferences through a refined, dark-themed settings menu.

## Supported Providers

| Provider | Reset Cycle | Key Discovery |
| :--- | :--- | :--- |
| **Anthropic (Claude)** | Balance/Credits | Env Vars, Manual |
| **DeepSeek** | Balance | Env Vars, Manual |
| **OpenAI** | Balance/Usage | Env Vars, Manual |
| **Gemini** | Daily / Minutely | Env Vars (API Key) |
| **Google Cloud Code** | OAuth Token | `gcloud` CLI status |
| **OpenRouter** | Credit Balance | Env Vars |
| **Antigravity** | Model-specific | Local App Detection |
| **Kimi (Moonshot)** | Balance | Local App Detection |
| **Z.AI** | Daily (24h) | Local App Detection |
| **Synthetic** | 5-Hour Cycle | Local App Detection |
| **OpenCode Zen** | 7-Day Cycle | Local App Detection |
| **GitHub Copilot** | Hourly Rate Limit | OAuth Device Flow |
| **Codex** | Model-specific | OAuth Device Flow |

## Installation

### Manual
1. Download the latest `AIConsumptionTracker_Setup.exe` from releases.
2. Run the installer.
3. The app will launch and automatically scan for common API keys.

## Configuration & Settings

Access the **Settings** menu by right-clicking the tray icon or using the gear icon on the dashboard.

### Application Settings
- **Show All Providers**: Toggle to show all configured providers, even those with 0 usage or errors.
- **Compact Mode**: Reduces the height of each item, removing the icon and condensing the layout.
- **Pin Window**: Keeps the dashboard open even when focus is lost.
- **Always On Top**: Ensures the dashboard floats above other windows.
- **Invert Progress Bars**: 
    - **Checked**: Bars represent **Remaining** capacity (Start Full/Green -> End Empty/Red).
    - **Unchecked**: Bars represent **Used** capacity (Start Empty -> End Full/Red).
- **Color Thresholds**: Customize the percentage at which bars turn Yellow (Warning) or Red (Critical).

### Provider Management
- **API Keys**: enter or update specific keys for each provider.
- **Track in Tray**: Check the box next to any provider to add a dedicated icon for it in your system tray.
- **Sub-Quotas**: For complex providers like Antigravity, you can pin specific model quotas to the tray.

## Storage
Configuration is stored in `auth.json` in the application data directory.
- **Automatic Backup**: Your previous configuration is preserved during updates.
- **Secure**: API keys are stored locally.

## License
MIT

