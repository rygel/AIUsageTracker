// <copyright file="ProviderAuthIdentityDiscoveryTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

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
        this._tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "provider-auth-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this._tempDirectory);
    }

    [Fact]
    public async Task TryGetGitHubUsernameAsync_ReadsHostsFileLoginAsync()
    {
        var hostsYaml =
            """
            github.com:
              user: octocat
            """;

        var hostsPath = this.CreateFile(
            "hosts.yml",
            hostsYaml);

        var username = await ProviderAuthIdentityDiscovery.TryGetGitHubUsernameAsync(
                this._logger,
                new[] { hostsPath });

        Assert.Equal("octocat", username);
    }

    [Fact]
    public async Task TryGetOpenAiUsernameAsync_ReadsOpenAiEmailClaimAsync()
    {
        var openAiAuthJson =
            """
            {
              "openai": {
                "email": "user@example.com"
              }
            }
            """;

        var authPath = this.CreateFile(
            "openai-auth.json",
            openAiAuthJson);

        var username = await ProviderAuthIdentityDiscovery.TryGetOpenAiUsernameAsync(
                this._logger,
                new[] { authPath });

        Assert.Equal("user@example.com", username);
    }

    [Fact]
    public async Task TryGetCodexUsernameAsync_ReadsNativeCodexJwtIdentityAsync()
    {
        var nativeCodexAuthJson =
            $$"""
            {
              "tokens": {
                "id_token": "{{this.CreateJwt(new { preferred_username = "codex@example.com" })}}"
              }
            }
            """;

        var authPath = this.CreateFile(
            "codex-auth.json",
            nativeCodexAuthJson);

        var username = await ProviderAuthIdentityDiscovery.TryGetCodexUsernameAsync(
                this._logger,
                new[] { authPath });

        Assert.Equal("codex@example.com", username);
    }

    [Fact]
    public async Task TryGetCodexUsernameAsync_IgnoresOpenCodeCompatibilityTokenAsync()
    {
        var openCodeCompatibilityAuthJson =
            $$"""
            {
              "openai": {
                "access": "{{this.CreateJwt(new { email = "openai@example.com" })}}"
              }
            }
            """;

        var authPath = this.CreateFile(
            "opencode-auth.json",
            openCodeCompatibilityAuthJson);

        var username = await ProviderAuthIdentityDiscovery.TryGetCodexUsernameAsync(
                this._logger,
                new[] { authPath });

        Assert.Null(username);
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
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    private string CreateJwt(object payload)
    {
        var header = this.Base64UrlEncode("""{"alg":"none","typ":"JWT"}""");
        var body = this.Base64UrlEncode(JsonSerializer.Serialize(payload));
        return $"{header}.{body}.";
    }

    private string Base64UrlEncode(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
