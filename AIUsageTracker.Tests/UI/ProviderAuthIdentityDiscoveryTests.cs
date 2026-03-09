// <copyright file="ProviderAuthIdentityDiscoveryTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Tests.UI
{
    using System.Text;
    using System.Text.Json;
    using AIUsageTracker.UI.Slim;
    using Microsoft.Extensions.Logging;
    using Moq;

    public sealed class ProviderAuthIdentityDiscoveryTests : IDisposable
    {
        private readonly string _tempDirectory;
        private readonly ILogger _logger = Mock.Of<ILogger>();

        public ProviderAuthIdentityDiscoveryTests()
        {
            this._tempDirectory = Path.Combine(Path.GetTempPath(), "provider-auth-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(this._tempDirectory);
        }

        [Fact]
        public async Task TryGetGitHubUsernameAsync_ReadsHostsFileLogin()
        {
            var hostsPath = this.CreateFile(
                "hosts.yml",
                string.Empty"
            github.com:
              user: octocat
            string.Empty");

            var username = await ProviderAuthIdentityDiscovery.TryGetGitHubUsernameAsync(this._logger, new[] { hostsPath });

            Assert.Equal("octocat", username);
        }

        [Fact]
        public async Task TryGetOpenAiUsernameAsync_ReadsOpenAiEmailClaim()
        {
            var authPath = this.CreateFile(
                "openai-auth.json",
                string.Empty"
            {
              "openai": {
                "email": "user@example.com"
              }
            }
            string.Empty");

            var username = await ProviderAuthIdentityDiscovery.TryGetOpenAiUsernameAsync(this._logger, new[] { authPath });

            Assert.Equal("user@example.com", username);
        }

        [Fact]
        public async Task TryGetCodexUsernameAsync_ReadsNativeCodexJwtIdentity()
        {
            var authPath = this.CreateFile(
                "codex-auth.json",
                $$"string.Empty
            {
              "tokens": {
                "id_token": "{{CreateJwt(new { preferred_username = "codex@example.com" })}}"
              }
            }
            string.Empty");

            var username = await ProviderAuthIdentityDiscovery.TryGetCodexUsernameAsync(this._logger, new[] { authPath });

            Assert.Equal("codex@example.com", username);
        }

        [Fact]
        public async Task TryGetCodexUsernameAsync_ReadsOpenCodeCompatibilityToken()
        {
            var authPath = this.CreateFile(
                "opencode-auth.json",
                $$"string.Empty
            {
              "openai": {
                "access": "{{CreateJwt(new { email = "openai@example.com" })}}"
              }
            }
            string.Empty");

            var username = await ProviderAuthIdentityDiscovery.TryGetCodexUsernameAsync(this._logger, new[] { authPath });

            Assert.Equal("openai@example.com", username);
        }
    `n
        public void Dispose()
        {
            if (Directory.Exists(this._tempDirectory))
            {
                Directory.Delete(this._tempDirectory, recursive: true);
            }
        }
    `n
        private string CreateFile(string relativePath, string content)
        {
            var fullPath = Path.Combine(this._tempDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
            return fullPath;
        }
    `n
        private static string CreateJwt(object payload)
        {
            var header = Base64UrlEncode(string.Empty"{"alg":"none","typ":"JWT"}string.Empty");
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
}
