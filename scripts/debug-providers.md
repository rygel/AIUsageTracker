# debug-providers.ps1

Captures provider HTTP JSON into `test-fixtures/` (or `-OutputDir`). Parameter is `-Providers` (string array).

```powershell
.\scripts\debug-providers.ps1
.\scripts\debug-providers.ps1 -Providers @("codex","kimi")
.\scripts\debug-providers.ps1 -OutputDir "test-fixtures\custom"
```

Hashtable keys in `$availableProviders`: `codex`, `kimi`, `anthropic`, `openai`, `openrouter`, `mistral`, `deepseek`, `zai`, `xiaomi`, `synthetic`, `opencode`, `minimax`, `github-copilot`, `claude-code`, `antigravity`. No `grok` entry.

Codex token: `%USERPROFILE%\.codex\auth.json`. Other keys: env vars in the script, plus `%USERPROFILE%\.ai-consumption-tracker\auth.json` (legacy path; not `DefaultAppPathProvider`).

Filenames: `{name}-{yyyy-MM-ddTHH-mm-ss}.json`.
