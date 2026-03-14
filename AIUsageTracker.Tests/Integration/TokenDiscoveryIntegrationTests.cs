// <copyright file="TokenDiscoveryIntegrationTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Infrastructure.Configuration;
using AIUsageTracker.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AIUsageTracker.Tests.Integration;

[Collection("TokenDiscovery")]
public class TokenDiscoveryIntegrationTests : IntegrationTestBase
{
    private readonly TokenDiscoveryService _service;

    public TokenDiscoveryIntegrationTests()
    {
        var mockPathProvider = new Mock<IAppPathProvider>();
        mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(this.TestRootPath);

        this._service = new TokenDiscoveryService(NullLogger<TokenDiscoveryService>.Instance, mockPathProvider.Object);
    }

    [Fact]
    public async Task DiscoverTokensAsync_FindsClaudeCodeTokenAsync()
    {
        // Arrange
        var content = "{\"claudeAiOauth\": {\"accessToken\": \"test-claude-token\"}}";
        this.CreateFile(".claude/.credentials.json", content);

        // Act
        var configs = await this._service.DiscoverTokensAsync();

        // Assert
        var config = configs.FirstOrDefault(c => string.Equals(c.ProviderId, "claude-code", StringComparison.Ordinal));
        Assert.NotNull(config);
        Assert.Equal("test-claude-token", config.ApiKey);
        Assert.Contains("Claude Code", config.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DiscoverTokensAsync_FindsKiloCodeSecretsAsync()
    {
        // Arrange
        var rooConfig = "{\"apiConfigs\": {\"default\": {\"openAiApiKey\": \"roo-openai-key\"}}}";
        var content = $"{{\"kilo code.kilo-code\": {{\"roo_cline_config_api_config\": {System.Text.Json.JsonSerializer.Serialize(rooConfig)}}}}}";
        this.CreateFile(".kilocode/secrets.json", content);

        // Act
        var configs = await this._service.DiscoverTokensAsync();

        // Assert
        var config = configs.FirstOrDefault(c => string.Equals(c.ProviderId, "openai", StringComparison.Ordinal));
        Assert.NotNull(config);
        Assert.Equal("roo-openai-key", config.ApiKey);
    }

    [Fact]
    public async Task DiscoverTokensAsync_DoesNotMapOpenCodeAuthToCodexAsync()
    {
        // Arrange
        var content = "{\"openai\": {\"access\": \"opencode-session-token\"}}";
        this.CreateFile(".opencode/auth.json", content);

        // Act
        var configs = await this._service.DiscoverTokensAsync();

        // Assert
        var config = configs.FirstOrDefault(c => string.Equals(c.ProviderId, "codex", StringComparison.Ordinal));
        Assert.True(config == null || string.IsNullOrWhiteSpace(config.ApiKey));
    }

    [Fact]
    public async Task DiscoverTokensAsync_FindsEnvironmentVariablesAsync()
    {
        // Arrange
        Environment.SetEnvironmentVariable("MINIMAX_API_KEY", "env-minimax-key");

        try
        {
            // Act
            var configs = await this._service.DiscoverTokensAsync();

            // Assert
            var config = configs.FirstOrDefault(c => string.Equals(c.ProviderId, "minimax", StringComparison.Ordinal));
            Assert.NotNull(config);
            Assert.Equal("env-minimax-key", config.ApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MINIMAX_API_KEY", null);
        }
    }

    [Fact]
    public async Task DiscoverTokensAsync_FindsEnvironmentVariableAliasesFromProviderMetadataAsync()
    {
        Environment.SetEnvironmentVariable("MOONSHOT_API_KEY", "env-kimi-key");

        try
        {
            var configs = await this._service.DiscoverTokensAsync();

            var config = configs.FirstOrDefault(c => string.Equals(c.ProviderId, "kimi-for-coding", StringComparison.Ordinal));
            Assert.NotNull(config);
            Assert.Equal("env-kimi-key", config.ApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MOONSHOT_API_KEY", null);
        }
    }

    [Fact]
    public async Task DiscoverTokensAsync_FindsStandaloneRooSecretsAsync()
    {
        var content = "{\"roo\":{\"apiConfigs\":{\"default\":{\"openAiApiKey\":\"roo-secret-key\"}}}}";
        this.CreateFile(".roo/secrets.json", content);

        var configs = await this._service.DiscoverTokensAsync();

        var config = configs.FirstOrDefault(c => string.Equals(c.ProviderId, "openai", StringComparison.Ordinal));
        Assert.NotNull(config);
        Assert.Equal("roo-secret-key", config.ApiKey);
        Assert.Equal("Roo Code: " + Path.Combine(this.TestRootPath, ".roo", "secrets.json"), config.AuthSource);
    }
}
