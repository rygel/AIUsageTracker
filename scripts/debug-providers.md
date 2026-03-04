# API Debug Script Infrastructure

A unified PowerShell tool to fetch and save API responses from multiple AI providers for testing and debugging.

## Usage

```powershell
# Fetch all configured providers
.\scripts\debug-providers.ps1

# Fetch specific provider
.\scripts\debug-providers.ps1 -Provider codex

# Fetch multiple providers
.\scripts\debug-providers.ps1 -Providers @("codex", "kimi", "anthropic")

# Custom output directory
.\scripts\debug-providers.ps1 -OutputDir "test-fixtures\custom"
```

## Supported Providers

| Provider | Environment Variable | API Endpoint |
|----------|---------------------|--------------|
| codex | (auto from ~/.codex/auth.json) | https://chatgpt.com/backend-api/wham/usage |
| kimi | KIMI_API_KEY | https://api.kimi.com/coding/v1/usages |
| anthropic | ANTHROPIC_API_KEY | https://api.anthropic.com/v1/usage |
| openai | OPENAI_API_KEY | https://api.openai.com/v1/usage |
| openrouter | OPENROUTER_API_KEY | https://openrouter.ai/api/v1/credits |
| mistral | MISTRAL_API_KEY | https://api.mistral.ai/v1/me |
| deepseek | DEEPSEEK_API_KEY | https://api.deepseek.com/user/balance |
| zai | ZAI_API_KEY | https://api.z.ai/api/monitor/usage/quota/limit |
| xiaomi | XIAOMI_API_KEY | https://api.xiaomimimo.com/v1/user/balance |
| synthetic | SYNTHETIC_API_KEY | (from config) |
| opencode | OPENCODE_API_KEY | (from config) |
| minimax | MINIMAX_API_KEY | (from config) |
| github-copilot | GITHUB_TOKEN | https://api.github.com/copilot_internal/usage |

## Output

Files are saved to `test-fixtures/` with timestamp format:
```
test-fixtures/codex-2026-03-04T17-30-00.json
test-fixtures/kimi-2026-03-04T17-30-00.json
```

## Requirements

- PowerShell 5.1+
- API keys set in environment variables (see table above)
- For Codex: access to `~/.codex/auth.json`

## Troubleshooting

### "No API key found"
- Set the corresponding environment variable
- Or add provider config to Monitor

### "Request failed"
- Check API key is valid
- Check network connectivity
- Verify API endpoint is correct
