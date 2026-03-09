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
        var hostsPath = this.CreateFile(
            "hosts.yml",
            """
            github.com:
              user: octocat
            """);

        var username = await ProviderAuthIdentityDiscovery.TryGetGitHubUsernameAsync(
                this._logger,
                new[] { hostsPath })
            .ConfigureAwait(false);

        Assert.Equal("octocat", username);
    }

    [Fact]
    public async Task TryGetOpenAiUsernameAsync_ReadsOpenAiEmailClaimAsync()
    {
        var authPath = this.CreateFile(
            "openai-auth.json",
            """
            {
              "openai": {
                "email": "user@example.com"
              }
            }
            """);

        var username = await ProviderAuthIdentityDiscovery.TryGetOpenAiUsernameAsync(
                this._logger,
                new[] { authPath })
            .ConfigureAwait(false);

        Assert.Equal("user@example.com", username);
    }

    [Fact]
    public async Task TryGetCodexUsernameAsync_ReadsNativeCodexJwtIdentityAsync()
    {
        var authPath = this.CreateFile(
            "codex-auth.json",
            $$"""
            {
              "tokens": {
                "id_token": "{{this.CreateJwt(new { preferred_username = "codex@example.com" })}}"
              }
            }
            """);

        var username = await ProviderAuthIdentityDiscovery.TryGetCodexUsernameAsync(
                this._logger,
                new[] { authPath })
            .ConfigureAwait(false);

        Assert.Equal("codex@example.com", username);
    }

    [Fact]
    public async Task TryGetCodexUsernameAsync_ReadsOpenCodeCompatibilityTokenAsync()
    {
        var authPath = this.CreateFile(
            "opencode-auth.json",
            $$"""
            {
              "openai": {
                "access": "{{this.CreateJwt(new { email = "openai@example.com" })}}"
              }
            }
            """);

        var username = await ProviderAuthIdentityDiscovery.TryGetCodexUsernameAsync(
                this._logger,
                new[] { authPath })
            .ConfigureAwait(false);

        Assert.Equal("openai@example.com", username);
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
