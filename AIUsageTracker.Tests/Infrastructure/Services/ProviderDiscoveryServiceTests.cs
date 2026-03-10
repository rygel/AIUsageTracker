using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure.Services;

public class ProviderDiscoveryServiceTests : IDisposable
{
    private readonly Mock<ILogger<ProviderDiscoveryService>> _mockLogger;
    private readonly ProviderDiscoveryService _service;
    private readonly string _tempTestDir;

    public ProviderDiscoveryServiceTests()
    {
        _mockLogger = new Mock<ILogger<ProviderDiscoveryService>>();
        _service = new ProviderDiscoveryService(_mockLogger.Object);
        _tempTestDir = Path.Combine(Path.GetTempPath(), "ProviderDiscoveryTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempTestDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempTestDir))
        {
            Directory.Delete(_tempTestDir, true);
        }
    }

    [Fact]
    public async Task DiscoverAuthAsync_FromEnvironmentVariable_ReturnsAuthData()
    {
        // Arrange
        var envVarName = "TEST_AUTH_KEY_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(envVarName, "test-api-key");
        
        var definition = new ProviderDefinition(
            "test-provider",
            "Test Provider",
            PlanType.Usage,
            false,
            "api_key",
            discoveryEnvironmentVariables: new[] { envVarName });

        try
        {
            // Act
            var result = await _service.DiscoverAuthAsync(definition);

            // Assert
            Assert.NotNull(result);
            Assert.Equal("test-api-key", result.AccessToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVarName, null);
        }
    }

    [Fact]
    public async Task DiscoverAuthAsync_FromAuthFile_ReturnsAuthData()
    {
        // Arrange
        var authFilePath = Path.Combine(_tempTestDir, "auth.json");
        var authContent = new
        {
            sessions = new
            {
                github = new
                {
                    user = "test-user",
                    oauth_token = "gho_test_token"
                }
            }
        };
        File.WriteAllText(authFilePath, JsonSerializer.Serialize(authContent));

        var definition = new ProviderDefinition(
            "github-copilot",
            "GitHub Copilot",
            PlanType.Coding,
            true,
            "session",
            authIdentityCandidatePathTemplates: new[] { authFilePath },
            sessionAuthFileSchemas: new[]
            {
                new ProviderAuthFileSchema(
                    "sessions.github",
                    "oauth_token",
                    "user")
            });

        // Act
        var result = await _service.DiscoverAuthAsync(definition);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("gho_test_token", result.AccessToken);
        Assert.Equal("test-user", result.AccountId);
        Assert.Equal(authFilePath, result.SourcePath);
    }

    [Fact]
    public async Task DiscoverAuthAsync_WhenNothingFound_ReturnsNull()
    {
        // Arrange
        var definition = new ProviderDefinition(
            "unknown-provider",
            "Unknown",
            PlanType.Usage,
            false,
            "api_key",
            discoveryEnvironmentVariables: new[] { "NON_EXISTENT_VAR" },
            authIdentityCandidatePathTemplates: new[] { Path.Combine(_tempTestDir, "non-existent.json") });

        // Act
        var result = await _service.DiscoverAuthAsync(definition);

        // Assert
        Assert.Null(result);
    }
}
