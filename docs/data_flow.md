# Data Flow Documentation

This document describes the current data flow for configuration, authentication, preferences, and monitor usage data.

## Configuration and Authentication (Read)

Provider configuration is loaded by `JsonConfigLoader` from `ConfigPathCatalog` in this order:

1. `%USERPROFILE%\.opencode\auth.json` (`IAppPathProvider.GetAuthFilePath()`)
2. `%LOCALAPPDATA%\AIUsageTracker\providers.json` (`IAppPathProvider.GetProviderConfigFilePath()`)

During merge:

- `auth.json` is treated as the key source.
- `providers.json` contributes provider metadata (`type`, `base_url`, tray flags, model config).
- Non-empty keys from auth source are preserved.

## Token Discovery (Fallback Read)

After file merge, `TokenDiscoveryService` fills missing keys from discovery sources:

1. Environment variables for supported providers.
2. Kilo Code / Roo Code token stores.
3. Claude Code and Codex native auth files.

Discovery augments missing keys; it does not overwrite already populated keys.

Provider-specific fallback coverage (metadata-enforced):

- `openai`: `OPENAI_API_KEY`, Roo `openAiApiKey`, OpenCode auth session files.
- `codex`: `CODEX_API_KEY`, Codex auth session files.
- `claude-code`: `ANTHROPIC_API_KEY`/`CLAUDE_API_KEY`, Claude credentials file.
- `gemini-cli`: `GEMINI_API_KEY`/`GOOGLE_API_KEY`, Roo `geminiApiKey`, plus Gemini CLI local files (section below).
- `deepseek`: `DEEPSEEK_API_KEY`, Roo `deepseekApiKey`.
- `openrouter`: `OPENROUTER_API_KEY`, Roo `openrouterApiKey`.
- `kimi`: `KIMI_API_KEY`/`MOONSHOT_API_KEY`.
- `xiaomi`: `XIAOMI_API_KEY`/`MIMO_API_KEY`.
- `minimax`: `MINIMAX_API_KEY`.
- `mistral`: Roo `mistralApiKey` and runtime env fallback in provider.
- `zai-coding-plan` (`zai`): `ZAI_API_KEY`/`Z_AI_API_KEY`, Roo `zaiApiKey`.
- `synthetic`: `SYNTHETIC_API_KEY`, Roo `syntheticApiKey`.
- `github-copilot`: external auth state via GitHub auth files/service.
- `antigravity`, `opencode-zen`: local runtime providers (process/CLI based, no API-key fallback chain).

## Gemini CLI Auth Flow

`gemini-cli` supports two local auth sources, in this strict order:

1. `%USERPROFILE%\.config\opencode\antigravity-accounts.json` (preferred when present; supports multiple accounts).
2. `%USERPROFILE%\.gemini\oauth_creds.json` + `%USERPROFILE%\.gemini\projects.json` (fallback for native Gemini CLI installs).

Gemini fallback details:

- `oauth_creds.json` provides `refresh_token` and `id_token`.
- Account identity is read from `id_token.email`; if missing, fallback to `%USERPROFILE%\.gemini\google_accounts.json` `active`.
- Project ID is resolved from `%USERPROFILE%\.gemini\projects.json`:
  - first choice: longest path match for the current working directory.
  - fallback: first available mapped project value.

If neither source yields a valid refresh token + project mapping, Gemini is shown as unavailable with `No Gemini accounts found`.

## Preferences (Read and Write)

Preferences are stored separately from auth:

- `%LOCALAPPDATA%\AIUsageTracker\preferences.json`

`SavePreferencesAsync` writes only the canonical preferences file and does not mutate auth/provider config files.

## Provider Config Persistence (Write)

When provider settings are saved:

1. Keys are written to `%USERPROFILE%\.opencode\auth.json`.
2. Non-secret provider settings are written to `%LOCALAPPDATA%\AIUsageTracker\providers.json`.

The export builder preserves existing per-provider payload structure where possible while updating the relevant fields.

## Monitor Data Flow

At runtime, the Monitor service:

1. Loads merged provider config via `IConfigLoader`.
2. Refreshes provider usage on schedule and on manual refresh.
3. Persists usage snapshots to `%LOCALAPPDATA%\AIUsageTracker\usage.db`.
4. Serves UI/Web clients via `/api/usage`, `/api/history`, `/api/config`, and related endpoints.

The Slim UI and Web UI read from monitor endpoints, not directly from provider APIs.
