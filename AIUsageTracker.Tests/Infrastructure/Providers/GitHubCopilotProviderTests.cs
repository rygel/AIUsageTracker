using System.Net;
using System.Text.Json;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Tests.Infrastructure;
using Moq;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class GitHubCopilotProviderTests : HttpProviderTestBase<GitHubCopilotProvider>
{
    private readonly GitHubCopilotProvider _provider;
    private readonly Mock<IGitHubAuthService> _authService;

    public GitHubCopilotProviderTests()
    {
        _authService = new Mock<IGitHubAuthService>();
        _provider = new GitHubCopilotProvider(HttpClient, Logger.Object, _authService.Object);
        Config.ApiKey = "test-key";
    }

    [Fact]
    public async Task GetUsageAsync_ValidResponse_ParsesQuotaCorrectly()
    {
        // Arrange
        _authService.Setup(s => s.GetCurrentToken()).Returns("test-key");

        var profileData = new { login = "user123" };
        var quotaData = new
        {
            copilot_plan = "copilot_individual",
            quota_snapshots = new
            {
                premium_interactions = new
                {
                    entitlement = 100.0,
                    remaining = 70.0
                }
            }
        };

        SetupHttpResponse("https://api.github.com/user", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(profileData))
        });

        SetupHttpResponse("https://api.github.com/copilot_internal/user", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(quotaData))
        });

        // Act
        var result = await _provider.GetUsageAsync(Config);

        // Assert
        var usage = result.Single();
        Assert.True(usage.IsAvailable);
        Assert.Equal("user123", usage.AccountName);
        Assert.Equal(70.0, usage.RequestsPercentage);
        Assert.Contains("Copilot Individual", usage.AuthSource);
    }
}
