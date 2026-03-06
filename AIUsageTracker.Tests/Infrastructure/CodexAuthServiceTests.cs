using AIUsageTracker.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace AIUsageTracker.Tests.Infrastructure;

public sealed class CodexAuthServiceTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly ILogger<CodexAuthService> _logger = Mock.Of<ILogger<CodexAuthService>>();

    public CodexAuthServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "codex-auth-service-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void GetAccessToken_ReadsNativeCodexAuth()
    {
        var authPath = CreateFile(
            "codex-auth.json",
            """
            {
              "tokens": {
                "access_token": "native-token",
                "account_id": "acct-native"
              }
            }
            """);

        var service = new CodexAuthService(_logger, authPath);

        Assert.Equal("native-token", service.GetAccessToken());
        Assert.Equal("acct-native", service.GetAccountId());
    }

    [Fact]
    public void GetAccessToken_ReadsCompatibilityAuth()
    {
        var authPath = CreateFile(
            "opencode-auth.json",
            """
            {
              "openai": {
                "access": "compat-token",
                "accountId": "acct-compat"
              }
            }
            """);

        var service = new CodexAuthService(_logger, authPath);

        Assert.Equal("compat-token", service.GetAccessToken());
        Assert.Equal("acct-compat", service.GetAccountId());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private string CreateFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(_tempDirectory, relativePath);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }
}
