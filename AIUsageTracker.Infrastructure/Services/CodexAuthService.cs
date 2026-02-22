using System.Text.Json;
using System.Text.Json.Serialization;
using AIUsageTracker.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIUsageTracker.Infrastructure.Services;

public class CodexAuthService : ICodexAuthService
{
    private readonly ILogger<CodexAuthService> _logger;
    private readonly string? _authFilePath;

    public CodexAuthService(ILogger<CodexAuthService> logger, string? authFilePath = null)
    {
        _logger = logger;
        _authFilePath = authFilePath;
    }

    public string? GetAccessToken()
    {
        var auth = LoadAuth();
        return auth?.AccessToken;
    }

    public string? GetAccountId()
    {
        var auth = LoadAuth();
        return auth?.AccountId;
    }

    private CodexAuth? LoadAuth()
    {
        foreach (var path in GetAuthFileCandidates())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                var json = File.ReadAllText(path);

                // Native Codex shape: { "tokens": { "access_token": "...", "account_id": "..." } }
                var nativeAuth = JsonSerializer.Deserialize<CodexNativeAuth>(json);
                if (!string.IsNullOrWhiteSpace(nativeAuth?.Tokens.AccessToken))
                {
                    return new CodexAuth
                    {
                        AccessToken = nativeAuth.Tokens.AccessToken,
                        AccountId = nativeAuth.Tokens.AccountId
                    };
                }

                // OpenCode shape: { "openai": { "access": "...", "accountId": "..." } }
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("openai", out var openai) &&
                    openai.ValueKind == JsonValueKind.Object)
                {
                    var access = openai.TryGetProperty("access", out var accessProp) && accessProp.ValueKind == JsonValueKind.String
                        ? accessProp.GetString()
                        : null;

                    if (!string.IsNullOrWhiteSpace(access))
                    {
                        var accountId = openai.TryGetProperty("accountId", out var accountIdProp) && accountIdProp.ValueKind == JsonValueKind.String
                            ? accountIdProp.GetString()
                            : null;

                        return new CodexAuth
                        {
                            AccessToken = access,
                            AccountId = accountId
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to read Codex auth file at {Path}", path);
            }
        }

        return null;
    }

    private IEnumerable<string> GetAuthFileCandidates()
    {
        if (!string.IsNullOrWhiteSpace(_authFilePath))
        {
            yield return _authFilePath;
            yield break;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, ".codex", "auth.json");
        yield return Path.Combine(home, ".local", "share", "opencode", "auth.json");
        yield return Path.Combine(home, ".opencode", "auth.json");

        if (OperatingSystem.IsWindows())
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(appData))
            {
                yield return Path.Combine(appData, "codex", "auth.json");
                yield return Path.Combine(appData, "opencode", "auth.json");
            }

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                yield return Path.Combine(localAppData, "opencode", "auth.json");
            }
        }
    }

    private sealed class CodexAuth
    {
        public string? AccessToken { get; set; }
        public string? AccountId { get; set; }
    }

    private sealed class CodexNativeAuth
    {
        [JsonPropertyName("tokens")]
        public CodexNativeTokens Tokens { get; set; } = new();
    }

    private sealed class CodexNativeTokens
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("account_id")]
        public string? AccountId { get; set; }
    }
}
