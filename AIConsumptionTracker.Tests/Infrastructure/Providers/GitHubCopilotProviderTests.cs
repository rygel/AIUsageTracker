using System.Net;
using System.Text.Json;
using AIConsumptionTracker.Core.Interfaces;
using AIConsumptionTracker.Core.Models;
using AIConsumptionTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace AIConsumptionTracker.Tests.Infrastructure.Providers;

public class GitHubCopilotProviderTests
{
    private readonly Mock<HttpMessageHandler> _msgHandler;
    private readonly HttpClient _httpClient;
    private readonly Mock<ILogger<GitHubCopilotProvider>> _logger;
    private readonly Mock<IGitHubAuthService> _authService;
    private readonly GitHubCopilotProvider _provider;

    public GitHubCopilotProviderTests()
    {
        _msgHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_msgHandler.Object);
        _logger = new Mock<ILogger<GitHubCopilotProvider>>();
        _authService = new Mock<IGitHubAuthService>();
        _provider = new GitHubCopilotProvider(_httpClient, _logger.Object, _authService.Object);
    }

    [Fact]
    public async Task GetUsageAsync_NoToken_ReturnsNotAuthenticated()
    {
        // Arrange
        _authService.Setup(x => x.GetCurrentToken()).Returns(string.Empty);
        var config = new ProviderConfig { ProviderId = "github-copilot" };

        // Act
        var result = await _provider.GetUsageAsync(config);

        // Assert
        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Contains("Not authenticated", usage.Description);
    }

    [Fact]
    public async Task GetUsageAsync_401Unauthorized_ReturnsAuthFailed()
    {
        // Arrange
        _authService.Setup(x => x.GetCurrentToken()).Returns("invalid-token");
        var config = new ProviderConfig { ProviderId = "github-copilot" };

        _msgHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString() == "https://api.github.com/user"),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.Unauthorized
            });

        // Act
        var result = await _provider.GetUsageAsync(config);

        // Assert
        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Contains("Authentication failed (401)", usage.Description);
    }

    [Fact]
    public async Task GetUsageAsync_HappyPath_ReturnsRateLimitAndPlan()
    {
        // Arrange
        _authService.Setup(x => x.GetCurrentToken()).Returns("valid-token");
        var config = new ProviderConfig { ProviderId = "github-copilot" };

        // 1. User profile
        _msgHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString() == "https://api.github.com/user"),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { login = "testuser" }))
            });

        // 2. Copilot Token (Plan info)
        _msgHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString() == "https://api.github.com/copilot_internal/v2/token"),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(new { sku = "copilot_business" }))
            });

        // 3. User usage (Billing) - fail or ignore
         _msgHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString() == "https://api.github.com/user/usage"),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage { StatusCode = HttpStatusCode.NotFound });

        // 4. Rate Limit
        var rateLimitResponse = JsonSerializer.Serialize(new
        {
            resources = new
            {
                core = new
                {
                    limit = 5000,
                    remaining = 4900,
                    reset = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
                }
            }
        });

        _msgHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.ToString() == "https://api.github.com/rate_limit"),
                ItExpr.IsAny<CancellationToken>()
            )
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(rateLimitResponse)
            });

        // Act
        var result = await _provider.GetUsageAsync(config);

        // Assert
        var usage = result.Single();
        Assert.True(usage.IsAvailable);
        Assert.Equal("GitHub Copilot", usage.ProviderName);
        Assert.Equal("testuser", usage.AccountName);
        Assert.Equal("Copilot Business", usage.AuthSource);
        Assert.Equal(98.0, usage.RequestsPercentage); // 4900/5000
        Assert.Contains("API Rate Limit: 4900/5000 Remaining", usage.Description);
        Assert.Contains("(Copilot Business)", usage.Description);
    }
}
