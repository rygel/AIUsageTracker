using AIUsageTracker.Infrastructure.Configuration;
using AIUsageTracker.Core.Interfaces;
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
        mockPathProvider.Setup(p => p.GetUserProfileRoot()).Returns(TestRootPath);

        _service = new TokenDiscoveryService(NullLogger<TokenDiscoveryService>.Instance, mockPathProvider.Object);
    }

    [Fact]
    public async Task DiscoverTokensAsync_FindsClaudeCodeToken()
    {
        // Arrange
        var content = "{\"claudeAiOauth\": {\"accessToken\": \"test-claude-token\"}}";
        CreateFile(".claude/.credentials.json", content);

        // Act
        var configs = await _service.DiscoverTokensAsync();

        // Assert
        var config = configs.FirstOrDefault(c => c.ProviderId == "claude-code");
        Assert.NotNull(config);
        Assert.Equal("test-claude-token", config.ApiKey);
        Assert.Contains("Claude Code", config.Description);
    }

    [Fact]
    public async Task DiscoverTokensAsync_FindsKiloCodeSecrets()
    {
        // Arrange
        var rooConfig = "{\"apiConfigs\": {\"default\": {\"openAiApiKey\": \"roo-openai-key\"}}}";
        var content = $"{{\"kilo code.kilo-code\": {{\"roo_cline_config_api_config\": {System.Text.Json.JsonSerializer.Serialize(rooConfig)}}}}}";
        CreateFile(".kilocode/secrets.json", content);

        // Act
        var configs = await _service.DiscoverTokensAsync();

        // Assert
        var config = configs.FirstOrDefault(c => c.ProviderId == "openai");
        Assert.NotNull(config);
        Assert.Equal("roo-openai-key", config.ApiKey);
    }

    [Fact]
    public async Task DiscoverTokensAsync_FindsOpenCodeAuth()
    {
        // Arrange
        var content = "{\"openai\": {\"access\": \"opencode-session-token\"}}";
        CreateFile(".opencode/auth.json", content);

        // Act
        var configs = await _service.DiscoverTokensAsync();

        // Assert
        var config = configs.FirstOrDefault(c => c.ProviderId == "codex");
        Assert.NotNull(config);
        Assert.Equal("opencode-session-token", config.ApiKey);
    }

    [Fact]
    public async Task DiscoverTokensAsync_FindsEnvironmentVariables()
    {
        // Arrange
        Environment.SetEnvironmentVariable("MINIMAX_API_KEY", "env-minimax-key");

        try
        {
            // Act
            var configs = await _service.DiscoverTokensAsync();

            // Assert
            var config = configs.FirstOrDefault(c => c.ProviderId == "minimax");
            Assert.NotNull(config);
            Assert.Equal("env-minimax-key", config.ApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MINIMAX_API_KEY", null);
        }
    }

    [Fact]
    public async Task DiscoverTokensAsync_FindsEnvironmentVariableAliasesFromProviderMetadata()
    {
        Environment.SetEnvironmentVariable("MOONSHOT_API_KEY", "env-kimi-key");

        try
        {
            var configs = await _service.DiscoverTokensAsync();

            var config = configs.FirstOrDefault(c => c.ProviderId == "kimi");
            Assert.NotNull(config);
            Assert.Equal("env-kimi-key", config.ApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MOONSHOT_API_KEY", null);
        }
    }

    [Fact]
    public async Task DiscoverTokensAsync_FindsStandaloneRooSecrets()
    {
        var content = "{\"roo\":{\"apiConfigs\":{\"default\":{\"openAiApiKey\":\"roo-secret-key\"}}}}";
        CreateFile(".roo/secrets.json", content);

        var configs = await _service.DiscoverTokensAsync();

        var config = configs.FirstOrDefault(c => c.ProviderId == "openai");
        Assert.NotNull(config);
        Assert.Equal("roo-secret-key", config.ApiKey);
        Assert.Equal("Roo Code: " + Path.Combine(TestRootPath, ".roo", "secrets.json"), config.AuthSource);
    }
}
