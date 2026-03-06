namespace AIUsageTracker.Core.Models;

public enum ProviderSettingsMode
{
    StandardApiKey,
    AutoDetectedStatus,
    ExternalAuthStatus,
    SessionAuthStatus
}

public enum ProviderSessionIdentitySource
{
    None,
    OpenAi,
    Codex
}
