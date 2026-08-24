# Environment variables

`ProviderDefinition.DiscoveryEnvironmentVariables` plus `MistralProvider` (`MISTRAL_API_KEY`, not on the definition). Scan also reads Kilo/Roo configs and session files (`TokenDiscoveryService`).

| Variable | Provider id |
|---|---|
| `ANTHROPIC_ADMIN_API_KEY` | `anthropic-usage` |
| `ANTHROPIC_API_KEY`, `CLAUDE_API_KEY` | `claude-code` |
| `CODEX_API_KEY` | `codex` |
| `DEEPSEEK_API_KEY` | `deepseek` |
| `GEMINI_API_KEY`, `GOOGLE_API_KEY` | `gemini-cli` |
| `GROQ_API_KEY` | `groq` |
| `KIMI_API_KEY`, `MOONSHOT_API_KEY` | `kimi-for-coding` |
| `MINIMAX_API_KEY` | `minimax` |
| `MINIMAX_IO_API_KEY` | `minimax-io` |
| `MINIMAX_CODING_PLAN_API_KEY` | `minimax-coding-plan` |
| `MISTRAL_API_KEY` | `mistral` |
| `OPENAI_API_KEY` | `openai` |
| `OPENCODE_API_KEY` | `opencode-go` |
| `OPENROUTER_API_KEY` | `openrouter` |
| `SYNTHETIC_API_KEY` | `synthetic` |
| `XIAOMI_API_KEY`, `MIMO_API_KEY` | `xiaomi` |
| `ZAI_API_KEY`, `Z_AI_API_KEY` | `zai-coding-plan` |

No discovery env vars on: `antigravity`, `github-copilot`, `grok` (`%USERPROFILE%\.grok\auth.json`), `opencode-zen`.

Persisted keys (`JsonConfigLoader.BuildConfigEntries`): later unique **auth** file wins. `GetAuthFilePath()` is `%USERPROFILE%\.opencode\auth.json`, which is also the first legacy OpenCode path, so de-dup keeps it first. Remaining unique order: `%USERPROFILE%\.config\opencode\auth.json`, `%APPDATA%\opencode\auth.json`, `%LOCALAPPDATA%\opencode\auth.json`, `%USERPROFILE%\.local\share\opencode\auth.json`, then `%LOCALAPPDATA%\AIUsageTracker\providers.json` (fills empty keys only; `IsAuthFile=false`), then `%LOCALAPPDATA%\AIUsageTracker\auth.json`.

`TokenDiscoveryService.DiscoverTokensAsync` then fills keys: env, Kilo, Roo, then session files (`AddOrUpdate` overwrites). Fetch-time `ProviderDiscoveryService.DiscoverAuthAsync` returns the first hit: env before session files.
