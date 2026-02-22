namespace AIUsageTracker.Core.Interfaces;

public interface ICodexAuthService
{
    string? GetAccessToken();
    string? GetAccountId();
}
