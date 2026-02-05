# AIConsumptionTracker CLI Documentation

The AIConsumptionTracker CLI allows you to monitor API usage and balance for various AI providers directly from your terminal.

## Basic Usage

```powershell
opencode-tracker <command> [options]
```

## Commands

### `status`
Displays the current usage status for all active providers. By default, it lists only those providers where a valid API key has been found or configured.

**Syntax:**
```bash
opencode-tracker status [options]
```

**Options:**
- `--all`: Show all configured providers, including those with missing API keys or those that are currently unavailable.
- `--json`: Output the status information in JSON format. This is useful for programmatic consumption or piping to other tools.

**Example Output (Table):**
```text
Provider                             | Type           | Used       | Description
--------------------------------------------------------------------------------------------------
OpenCode                             | Pay-As-You-Go  | 15%        | 150.00 / 1000.00 credits
Anthropic                            | Pay-As-You-Go  | 0%         | Not Found
Minimax                              | Pay-As-You-Go  | 0%         | Discovered via Environment Variable
```

### `list`
Lists all configured providers found in the configuration files or environment variables.

**Syntax:**
```bash
opencode-tracker list [options]
```

**Options:**
- `--json`: Output the list in JSON format.

## Configuration

### File-Based Configuration
The CLI looks for a configuration file named `auth.json` in the following locations:
1. `%USERPROFILE%\.local\share\opencode\auth.json`
2. `%APPDATA%\opencode\auth.json`
3. `%LOCALAPPDATA%\opencode\auth.json`
4. `%USERPROFILE%\.opencode\auth.json`

**Format:**
```json
{
  "provider-id": {
    "key": "your-api-key",
    "type": "pay-as-you-go" // or "quota-based"
  }
}
```

### Environment Variables
You can also configure specific providers securely using environment variables. These are discovered automatically and do not need to be written to a file.

| Provider | Environment Variable | Alternate Variable |
| :--- | :--- | :--- |
| **Minimax** | `MINIMAX_API_KEY` | |
| **Xiaomi** | `XIAOMI_API_KEY` | `MIMO_API_KEY` |
| **Kimi (Moonshot)** | `KIMI_API_KEY` | `MOONSHOT_API_KEY` |

## Supported Providers
- **Standard**: OpenCode, OpenRouter, Anthropic, Gemini, OpenAI (Generic)
- **Chinese LLMs**: Minimax, Xiaomi (MiMo), Kimi (Moonshot)
- **Generic**: Any provider interacting via standard HTTP headers can be configured manually.

