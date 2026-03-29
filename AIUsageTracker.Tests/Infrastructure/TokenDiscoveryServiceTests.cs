// <copyright file="TokenDiscoveryServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace AIUsageTracker.Tests.Infrastructure;

public class TokenDiscoveryServiceTests
{
    [Fact]
    public async Task DiscoverTokensAsync_DoesNotExpandWellKnownProviderAliasesAsync()
    {
        var mockPathProvider = new Mock<IAppPathProvider>();
        mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(Path.GetTempPath());
        var discovery = new TokenDiscoveryService(NullLogger<TokenDiscoveryService>.Instance, mockPathProvider.Object);

        var configs = await discovery.DiscoverTokensAsync();

        Assert.DoesNotContain(configs, c => c.ProviderId.Equals("minimax-io", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(configs, c => c.ProviderId.Equals("minimax-global", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(configs, c => c.ProviderId.Equals("kimi", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DiscoverTokensAsync_DoesNotReadProvidersConfigAsTokenSourceAsync()
    {
        var testRoot = TestTempPaths.CreateDirectory("token-discovery");

        try
        {
            var providerConfigPath = Path.Combine(testRoot, "providers.json");
            var json = JsonSerializer.Serialize(new
            {
                openai = new
                {
                    key = "config-only-key",
                },
            });
            await File.WriteAllTextAsync(providerConfigPath, json);

            var mockPathProvider = new Mock<IAppPathProvider>();
            mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(testRoot);
            mockPathProvider.Setup(p => p.GetProviderConfigFilePath()).Returns(providerConfigPath);

            var discovery = new TokenDiscoveryService(NullLogger<TokenDiscoveryService>.Instance, mockPathProvider.Object);

            var configs = await discovery.DiscoverTokensAsync();

            var openAi = configs.FirstOrDefault(c => c.ProviderId.Equals("openai", StringComparison.OrdinalIgnoreCase));
            Assert.True(
                openAi == null ||
                string.IsNullOrEmpty(openAi.ApiKey) ||
                !string.Equals(openAi.ApiKey, "config-only-key", StringComparison.Ordinal),
                "Token discovery should not read providers.json as an auth source.");
        }
        finally
        {
            TestTempPaths.CleanupPath(testRoot);
        }
    }

    [Fact]
    public async Task DiscoverTokensAsync_DoesNotReadCanonicalAuthConfigAsSessionTokenSourceAsync()
    {
        var testRoot = TestTempPaths.CreateDirectory("token-discovery");

        try
        {
            var authConfigPath = Path.Combine(testRoot, "auth.json");
            var json = JsonSerializer.Serialize(new
            {
                openai = new
                {
                    access = "config-file-session-token",
                },
            });
            await File.WriteAllTextAsync(authConfigPath, json);

            var mockPathProvider = new Mock<IAppPathProvider>();
            mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(testRoot);
            mockPathProvider.Setup(p => p.GetAuthFilePath()).Returns(authConfigPath);

            var discovery = new TokenDiscoveryService(NullLogger<TokenDiscoveryService>.Instance, mockPathProvider.Object);

            var configs = await discovery.DiscoverTokensAsync();

            var codex = configs.FirstOrDefault(c => c.ProviderId.Equals("codex", StringComparison.OrdinalIgnoreCase));
            Assert.True(codex == null || string.IsNullOrEmpty(codex.ApiKey));
        }
        finally
        {
            TestTempPaths.CleanupPath(testRoot);
        }
    }

    [Fact]
    public async Task DiscoverTokensAsync_DiscoversCodexSessionTokenFromSessionAuthFileAsync()
    {
        var testRoot = TestTempPaths.CreateDirectory("token-discovery-codex-session");

        try
        {
            var codexPath = Path.Combine(testRoot, ".codex", "auth.json");
            Directory.CreateDirectory(Path.GetDirectoryName(codexPath)!);
            await File.WriteAllTextAsync(codexPath, "{\"tokens\":{\"access_token\":\"codex-session-token\"}}");

            var mockPathProvider = new Mock<IAppPathProvider>();
            mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(testRoot);

            var discovery = new TokenDiscoveryService(NullLogger<TokenDiscoveryService>.Instance, mockPathProvider.Object);
            var configs = await discovery.DiscoverTokensAsync();

            var codex = configs.FirstOrDefault(c => c.ProviderId.Equals("codex", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(codex);
            Assert.Equal("codex-session-token", codex!.ApiKey);
            Assert.Contains(".codex", codex.AuthSource, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TestTempPaths.CleanupPath(testRoot);
        }
    }

    [Fact]
    public async Task DiscoverTokensAsync_CodexSessionFileOverridesEnvironmentVariableAsync()
    {
        var testRoot = TestTempPaths.CreateDirectory("token-discovery-codex-precedence");
        Environment.SetEnvironmentVariable("CODEX_API_KEY", "env-codex-key");

        try
        {
            var codexPath = Path.Combine(testRoot, ".codex", "auth.json");
            Directory.CreateDirectory(Path.GetDirectoryName(codexPath)!);
            await File.WriteAllTextAsync(codexPath, "{\"tokens\":{\"access_token\":\"file-codex-key\"}}");

            var mockPathProvider = new Mock<IAppPathProvider>();
            mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(testRoot);

            var discovery = new TokenDiscoveryService(NullLogger<TokenDiscoveryService>.Instance, mockPathProvider.Object);
            var configs = await discovery.DiscoverTokensAsync();

            var codex = configs.FirstOrDefault(c => c.ProviderId.Equals("codex", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(codex);
            Assert.Equal("file-codex-key", codex!.ApiKey);
            Assert.Contains(".codex", codex.AuthSource, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_API_KEY", null);
            TestTempPaths.CleanupPath(testRoot);
        }
    }

    [Fact]
    public async Task DiscoverTokensAsync_DiscoversClaudeSessionTokenFromCredentialsFileAsync()
    {
        var testRoot = TestTempPaths.CreateDirectory("token-discovery-claude-session");

        try
        {
            var claudePath = Path.Combine(testRoot, ".claude", ".credentials.json");
            Directory.CreateDirectory(Path.GetDirectoryName(claudePath)!);
            await File.WriteAllTextAsync(claudePath, "{\"claudeAiOauth\":{\"accessToken\":\"claude-session-token\"}}");

            var mockPathProvider = new Mock<IAppPathProvider>();
            mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(testRoot);

            var discovery = new TokenDiscoveryService(NullLogger<TokenDiscoveryService>.Instance, mockPathProvider.Object);
            var configs = await discovery.DiscoverTokensAsync();

            var claude = configs.FirstOrDefault(c => c.ProviderId.Equals("claude-code", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(claude);
            Assert.Equal("claude-session-token", claude!.ApiKey);
            Assert.Contains(".claude", claude.AuthSource, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TestTempPaths.CleanupPath(testRoot);
        }
    }

    [Theory]
    [InlineData("gemini-cli", "GEMINI_API_KEY", "geminiApiKey")]
    [InlineData("deepseek", "DEEPSEEK_API_KEY", "deepseekApiKey")]
    [InlineData("synthetic", "SYNTHETIC_API_KEY", "syntheticApiKey")]
    [InlineData("zai-coding-plan", "ZAI_API_KEY", "zaiApiKey")]
    public async Task DiscoverTokensAsync_EnvironmentVariableTakesPrecedenceOverRooSecretsAsync(
        string providerId,
        string environmentVariableName,
        string rooPropertyName)
    {
        var testRoot = TestTempPaths.CreateDirectory($"token-discovery-precedence-{providerId}");
        var envValue = $"{providerId}-env-key";
        var rooValue = $"{providerId}-roo-key";
        Environment.SetEnvironmentVariable(environmentVariableName, envValue);

        try
        {
            var rooPath = Path.Combine(testRoot, ".roo", "secrets.json");
            Directory.CreateDirectory(Path.GetDirectoryName(rooPath)!);
            var rooJson = $$"""
                            {
                              "roo": {
                                "apiConfigs": {
                                  "default": {
                                    "{{rooPropertyName}}": "{{rooValue}}"
                                  }
                                }
                              }
                            }
                            """;
            await File.WriteAllTextAsync(rooPath, rooJson);

            var mockPathProvider = new Mock<IAppPathProvider>();
            mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(testRoot);

            var discovery = new TokenDiscoveryService(NullLogger<TokenDiscoveryService>.Instance, mockPathProvider.Object);
            var configs = await discovery.DiscoverTokensAsync();

            var config = configs.FirstOrDefault(c => c.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(config);
            Assert.Equal(envValue, config!.ApiKey);
            Assert.StartsWith("Env:", config.AuthSource, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariableName, null);
            TestTempPaths.CleanupPath(testRoot);
        }
    }

    [Theory]
    [InlineData("gemini-cli", "geminiApiKey")]
    [InlineData("deepseek", "deepseekApiKey")]
    [InlineData("synthetic", "syntheticApiKey")]
    [InlineData("zai-coding-plan", "zaiApiKey")]
    public async Task DiscoverTokensAsync_UsesRooFallbackWhenEnvironmentVariableIsMissingAsync(
        string providerId,
        string rooPropertyName)
    {
        var testRoot = TestTempPaths.CreateDirectory($"token-discovery-roo-fallback-{providerId}");
        var rooValue = $"{providerId}-roo-only-key";

        try
        {
            var rooPath = Path.Combine(testRoot, ".roo", "secrets.json");
            Directory.CreateDirectory(Path.GetDirectoryName(rooPath)!);
            var rooJson = $$"""
                            {
                              "roo": {
                                "apiConfigs": {
                                  "default": {
                                    "{{rooPropertyName}}": "{{rooValue}}"
                                  }
                                }
                              }
                            }
                            """;
            await File.WriteAllTextAsync(rooPath, rooJson);

            var mockPathProvider = new Mock<IAppPathProvider>();
            mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(testRoot);

            var discovery = new TokenDiscoveryService(NullLogger<TokenDiscoveryService>.Instance, mockPathProvider.Object);
            var configs = await discovery.DiscoverTokensAsync();

            var config = configs.FirstOrDefault(c => c.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(config);
            Assert.Equal(rooValue, config!.ApiKey);
            Assert.Contains("Roo", config.AuthSource, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TestTempPaths.CleanupPath(testRoot);
        }
    }
}
