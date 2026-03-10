// <copyright file="CodexAuthServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

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
        this._tempDirectory = Path.Combine(Path.GetTempPath(), "codex-auth-service-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this._tempDirectory);
    }

    [Fact]
    public void GetAccessToken_ReadsNativeCodexAuth()
    {
        var nativeAuthJson =
            """
            {
              "tokens": {
                "access_token": "native-token",
                "account_id": "acct-native"
              }
            }
            """;

        var authPath = this.CreateFile(
            "codex-auth.json",
            nativeAuthJson);

        var service = new CodexAuthService(this._logger, authPath);

        Assert.Equal("native-token", service.GetAccessToken());
        Assert.Equal("acct-native", service.GetAccountId());
    }

    [Fact]
    public void GetAccessToken_IgnoresCompatibilityAuth()
    {
        var compatibilityAuthJson =
            """
            {
              "openai": {
                "access": "compat-token",
                "accountId": "acct-compat"
              }
            }
            """;

        var authPath = this.CreateFile(
            "opencode-auth.json",
            compatibilityAuthJson);

        var service = new CodexAuthService(this._logger, authPath);

        Assert.Null(service.GetAccessToken());
        Assert.Null(service.GetAccountId());
    }

    public void Dispose()
    {
        if (Directory.Exists(this._tempDirectory))
        {
            Directory.Delete(this._tempDirectory, recursive: true);
        }
    }

    private string CreateFile(string relativePath, string content)
    {
        var fullPath = Path.Combine(this._tempDirectory, relativePath);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }
}
