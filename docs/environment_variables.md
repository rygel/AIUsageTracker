# Environment Variables

AI Consumption Tracker can automatically discover API keys from environment variables. Set these variables before launching the application to have your providers configured without manual input.

## Setting Environment Variables

### Windows (Command Prompt)
```cmd
set OPENAI_API_KEY=sk-your-key-here
set ANTHROPIC_API_KEY=sk-ant-your-key-here
```

### Windows (PowerShell)
```powershell
$env:OPENAI_API_KEY = "sk-your-key-here"
$env:ANTHROPIC_API_KEY = "sk-ant-your-key-here"
```

### Windows (Permanent via System Properties)
1. Press `Win + R`, type `sysdm.cpl`
2. Go to **Advanced** tab → **Environment Variables**
3. Add new variables under **User variables** or **System variables**

### Linux/macOS (Bash)
```bash
export OPENAI_API_KEY=sk-your-key-here
export ANTHROPIC_API_KEY=sk-ant-your-key-here
```

Add to `~/.bashrc`, `~/.zshrc`, or `~/.profile` for persistence.

### Linux/macOS (Fish)
```fish
set -x OPENAI_API_KEY sk-your-key-here
set -x ANTHROPIC_API_KEY sk-ant-your-key-here
```

## Supported Environment Variables

| Environment Variable | Provider ID | Provider Name | Notes |
|---|---|---|---|
| `OPENAI_API_KEY` | `openai` | OpenAI | Standard user API keys (shows "Connected - Check Dashboard") |
| OpenAI JWT | `openai` | OpenAI | From OpenCode CLI login (shows actual usage & balance) |
| `ANTHROPIC_API_KEY`<br>`CLAUDE_API_KEY` | `claude-code` | Anthropic/Claude | Either variable works |
| `GEMINI_API_KEY`<br>`GOOGLE_API_KEY` | `gemini-cli` | Google Gemini | Either variable works |
| `DEEPSEEK_API_KEY` | `deepseek` | DeepSeek | - |
| `OPENROUTER_API_KEY` | `openrouter` | OpenRouter | - |
| `KIMI_API_KEY`<br>`MOONSHOT_API_KEY` | `kimi` | Kimi/Moonshot | Either variable works |
| `XIAOMI_API_KEY`<br>`MIMO_API_KEY` | `xiaomi` | Xiaomi/Mimo | Either variable works |
| `MINIMAX_API_KEY` | `minimax` | Minimax | - |
| `ZAI_API_KEY`<br>`Z_AI_API_KEY` | `zai` | Z.AI | Either variable works |
| `ANTIGRAVITY_API_KEY`<br>`GOOGLE_ANTIGRAVITY_API_KEY` | `antigravity` | Google Antigravity | Either variable works |
| `OPENCODE_API_KEY` | `opencode` | OpenCode | - |
| `OPENCODE_ZEN_API_KEY` | `opencode-zen` | OpenCode Zen | - |
| `CLOUDCODE_API_KEY` | `cloudcode` | CloudCode | - |
| `CODEX_API_KEY` | `codex` | Codex | - |

## Priority Order

When multiple sources are available, keys are loaded in this order:

1. **Configuration Files** (`auth.json`)
2. **Environment Variables**
3. **Kilo Code Secrets** (for Roo Cline integration)
4. **Providers Definition** (`providers.json`)

The first available key for each provider is used. Environment variables will override Kilo Code secrets but not configuration files.

## Security Considerations

- **Never commit** environment variables to version control
- Use `.env` files (with proper exclusions in `.gitignore`) for development
- Keep API keys secure and rotate them regularly
- Use read-only keys when available
- On shared systems, prefer user-level environment variables over system-level

## Examples

### Example .env file
```env
OPENAI_API_KEY=sk-proj-abc123
ANTHROPIC_API_KEY=sk-ant-xyz789
GEMINI_API_KEY=AIzaSyAbCdEfGhIjKlMnOpQrStUvWxYz
```

Load with: `source .env` (Linux/macOS) or via dotenv in applications.

### Example PowerShell Script
```powershell
# config.ps1
$env:OPENAI_API_KEY = "sk-proj-abc123"
$env:ANTHROPIC_API_KEY = "sk-ant-xyz789"

# Start application
.\AIConsumptionTracker.exe
```

### Example Bash Script
```bash
#!/bin/bash
# launch.sh
export OPENAI_API_KEY="sk-proj-abc123"
export ANTHROPIC_API_KEY="sk-ant-xyz789"

# Start application
./ai-consumption-tracker
```

## Troubleshooting

### Environment variables not being detected
- Verify variable names exactly match (case-sensitive on Linux/macOS)
- Restart the application after setting variables
- Check for typos in variable names
- Ensure no trailing spaces in values

### Keys work in other apps but not here
- Verify the provider ID matches the expected environment variable
- Check that the API key format is correct for the provider
- Ensure the key has not expired or been revoked
- Try removing and re-adding the environment variable

### Windows vs Linux/macOS
- Windows: Environment variable names are case-insensitive
- Linux/macOS: Environment variable names are case-sensitive
- Always use uppercase names for consistency
