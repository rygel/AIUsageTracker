using System.Text;
using System.Text.Json;
using AIUsageTracker.UI.Slim;
using Microsoft.Extensions.Logging;
using Moq;

namespace AIUsageTracker.Tests.UI;

public sealed class ProviderAuthIdentityDiscoveryTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly ILogger _logger = Mock.Of<ILogger>();

    public ProviderAuthIdentityDiscoveryTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "provider-auth-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task TryGetGitHubUsernameAsync_ReadsHostsFileLogin()
    {
        var hostsPath = CreateFile(
            "hosts.yml",
            """
            github.com:
              user: octocat
            """);

        var username = await ProviderAuthIdentityDiscovery.TryGetGitHubUsernameAsync(_logger, new[] { hostsPath });

        Assert.Equal("octocat", username);
    }

    [Fact]
    public async Task TryGetOpenAiUsernameAsync_ReadsOpenAiEmailClaim()
    {
        var authPath = CreateFile(
            "openai-auth.json",
            """
            {
              "openai": {
                "email": "user@example.com"
              }
            }
            """);

        var username = await ProviderAuthIdentityDiscovery.TryGetOpenAiUsernameAsync(_logger, new[] { authPath });

        Assert.Equal("user@example.com", username);
    }

    [Fact]
    public async Task TryGetCodexUsernameAsync_ReadsNativeCodexJwtIdentity()
    {
        var authPath = CreateFile(
            "codex-auth.json",
            $$"""
            {
              "tokens": {
                "id_token": "{{CreateJwt(new { preferred_username = "codex@example.com" })}}"
              }
            }
            """);

        var username = await ProviderAuthIdentityDiscovery.TryGetCodexUsernameAsync(_logger, new[] { authPath });

        Assert.Equal("codex@example.com", username);
    }

    [Fact]
    public async Task TryGetCodexUsernameAsync_ReadsOpenCodeCompatibilityToken()
    {
        var authPath = CreateFile(
            "opencode-auth.json",
            $$"""
            {
              "openai": {
                "access": "{{CreateJwt(new { email = "openai@example.com" })}}"
              }
            }
            """);

        var username = await ProviderAuthIdentityDiscovery.TryGetCodexUsernameAsync(_logger, new[] { authPath });

        Assert.Equal("openai@example.com", username);
    }
`n
    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
`n
    private string CreateFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }
`n
    private static string CreateJwt(object payload)
    {
        var header = Base64UrlEncode("""{"alg":"none","typ":"JWT"}""");
        var body = Base64UrlEncode(JsonSerializer.Serialize(payload));
        return $"{header}.{body}.";
    }
`n
    private static string Base64UrlEncode(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
