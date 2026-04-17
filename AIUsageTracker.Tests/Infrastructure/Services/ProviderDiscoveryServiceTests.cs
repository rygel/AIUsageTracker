using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace AIUsageTracker.Tests.Infrastructure.Services;

public class ProviderDiscoveryServiceTests : IDisposable
{
    private readonly Mock<ILogger<ProviderDiscoveryService>> _mockLogger;
    private readonly Mock<IAppPathProvider> _mockPathProvider;
    private readonly ProviderDiscoveryService _service;
    private readonly string _tempTestDir;

    public ProviderDiscoveryServiceTests()
    {
        this._tempTestDir = TestTempPaths.CreateDirectory("ProviderDiscoveryTests");
        this._mockLogger = new Mock<ILogger<ProviderDiscoveryService>>();
        this._mockPathProvider = new Mock<IAppPathProvider>();
        this._mockPathProvider.Setup(pathProvider => pathProvider.GetUserProfileRoot()).Returns(this._tempTestDir);
        this._service = new ProviderDiscoveryService(this._mockLogger.Object, this._mockPathProvider.Object);
    }

    public void Dispose()
    {
        TestTempPaths.CleanupPath(this._tempTestDir);
    }

    [Fact]
    public async Task DiscoverAuthAsync_FromEnvironmentVariable_ReturnsAuthDataAsync()
    {
        // Arrange
        var envVarName = "TEST_AUTH_KEY_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(envVarName, "test-api-key");

        var definition = new ProviderDefinition(
            "test-provider",
            "Test Provider",
            PlanType.Usage,
            isQuotaBased: false)
        {
            DiscoveryEnvironmentVariables = new[] { envVarName },
        };

        try
        {
            // Act
            var result = await this._service.DiscoverAuthAsync(definition.CreateAuthDiscoverySpec());

            // Assert
            Assert.NotNull(result);
            Assert.Equal("test-api-key", result.AccessToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, value: null);
        }
    }

    [Fact]
    public async Task DiscoverAuthAsync_FromAuthFile_ReturnsAuthDataAsync()
    {
        // Arrange
        var authFilePath = Path.Combine(this._tempTestDir, "auth.json");
        var authContent = new
        {
            sessions = new
            {
                github = new
                {
                    user = "test-user",
                    oauth_token = "gho_test_token",
                },
            },
        };
        File.WriteAllText(authFilePath, JsonSerializer.Serialize(authContent));

        var definition = new ProviderDefinition(
            "github-copilot",
            "GitHub Copilot",
            PlanType.Coding,
            true)
        {
            AuthIdentityCandidatePathTemplates = new[] { authFilePath },
            SessionAuthFileSchemas = new[]
            {
                new ProviderAuthFileSchema("sessions.github", "oauth_token", "user"),
            },
        };

        // Act
        var result = await this._service.DiscoverAuthAsync(definition.CreateAuthDiscoverySpec());

        // Assert
        Assert.NotNull(result);
        Assert.Equal("gho_test_token", result.AccessToken);
        Assert.Equal("test-user", result.AccountId);
        Assert.Equal(authFilePath, result.SourcePath);
    }

    [Fact]
    public async Task DiscoverAuthAsync_WhenNothingFound_ReturnsNullAsync()
    {
        // Arrange
        var definition = new ProviderDefinition(
            "unknown-provider",
            "Unknown",
            PlanType.Usage,
            false)
        {
            DiscoveryEnvironmentVariables = new[] { "NON_EXISTENT_VAR" },
            AuthIdentityCandidatePathTemplates = new[] { Path.Combine(this._tempTestDir, "non-existent.json") },
        };

        // Act
        var result = await this._service.DiscoverAuthAsync(definition.CreateAuthDiscoverySpec());

        // Assert
        Assert.Null(result);
    }
}
