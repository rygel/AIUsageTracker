# Test fixture sync

Fixtures must match real provider payloads (secrets redacted). Update tests first, then screenshot fixtures, in the same PR.

## Locations

- `AIUsageTracker.Tests/TestData/Providers/`
- Inline payloads in `AIUsageTracker.Tests/Infrastructure/Providers/*ProviderTests.cs`
- Screenshot data in `AIUsageTracker.UI.Slim/MainWindow.xaml.cs` and `SettingsWindow.xaml.cs`

## Snapshots

`antigravity_user_status.snapshot.json`, `codex_rate_limit_reset_credits.snapshot.json`, `codex_wham_usage.snapshot.json`, `gemini_cli_retrieve_user_quota.snapshot.json`, `github_copilot_rate_limit.snapshot.json`, `github_copilot_internal_user.snapshot.json`, `github_copilot_internal_v2_token_404.snapshot.json`, `grok_billing_credits.snapshot.json` (Grok CLI 1.0.5 `GET https://cli-chat-proxy.grok.com/v1/billing?format=credits`, 2026-08-21; percentages/timestamps normalized).

Antigravity screenshot models must match the snapshot labels: Claude Opus 4.6 (Thinking), Claude Sonnet 4.6 (Thinking), Gemini 3 Flash, Gemini 3.1 Pro (High/Low), GPT-OSS 120B (Medium).

```powershell
dotnet run --project .\AIUsageTracker.UI.Slim\AIUsageTracker.UI.Slim.csproj --configuration Release -- --test --screenshot
pwsh -NoProfile -ExecutionPolicy Bypass -File .\scripts\verify-doc-images.ps1
```
