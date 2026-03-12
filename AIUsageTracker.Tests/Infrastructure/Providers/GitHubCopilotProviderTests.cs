// <copyright file="GitHubCopilotProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

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
    private readonly Mock<IProviderDiscoveryService> _discoveryService;

    public GitHubCopilotProviderTests()
    {
        this._authService = new Mock<IGitHubAuthService>();
        this._discoveryService = new Mock<IProviderDiscoveryService>();
        this._provider = new GitHubCopilotProvider(this.ResilientHttpClient.Object, this.Logger.Object, this._authService.Object, this._discoveryService.Object);
        this.Config.ApiKey = "test-key";
    }

    [Fact]
    public async Task GetUsageAsync_ValidResponse_ParsesQuotaCorrectlyAsync()
    {
        // Arrange
        this._authService.Setup(s => s.GetCurrentToken()).Returns("test-key");

        var profileData = new { login = "user123" };
        var quotaData = new
        {
            copilot_plan = "copilot_individual",
            quota_snapshots = new
            {
                premium_interactions = new
                {
                    entitlement = 100.0,
                    remaining = 70.0,
                },
            },
        };

        this.SetupHttpResponse("https://api.github.com/user", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(profileData)),
        });

        this.SetupHttpResponse("https://api.github.com/copilot_internal/v2/token", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("{\"sku\":\"copilot_individual\"}"),
        });

        this.SetupHttpResponse("https://api.github.com/copilot_internal/user", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(quotaData)),
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.True(usage.IsAvailable);
        Assert.Equal("user123", usage.AccountName);
        Assert.Equal(70.0, usage.RequestsPercentage);
        Assert.Contains("Copilot Individual", usage.AuthSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_BothQuotaWindows_PrefersWeeklyPremiumWindowForTopLevelAsync()
    {
        // Arrange
        this._authService.Setup(s => s.GetCurrentToken()).Returns("test-key");

        var profileData = new { login = "user123" };
        var quotaData = new
        {
            copilot_plan = "copilot_individual",
            quota_snapshots = new
            {
                usage = new
                {
                    entitlement = 100.0,
                    remaining = 80.0,
                },
                premium_interactions = new
                {
                    entitlement = 100.0,
                    remaining = 20.0,
                },
            },
        };

        this.SetupHttpResponse("https://api.github.com/user", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(profileData)),
        });

        this.SetupHttpResponse("https://api.github.com/copilot_internal/v2/token", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("{\"sku\":\"copilot_individual\"}"),
        });

        this.SetupHttpResponse("https://api.github.com/copilot_internal/user", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(quotaData)),
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = Assert.Single(result);
        Assert.True(usage.IsAvailable);
        Assert.Equal(20.0, usage.RequestsPercentage);
        Assert.Equal(80.0, usage.RequestsUsed);
        Assert.Equal(100.0, usage.RequestsAvailable);
        Assert.Contains("Weekly Quota", usage.Description, StringComparison.Ordinal);
        Assert.NotNull(usage.Details);
        Assert.Contains(usage.Details!, detail => detail.Name == "5-hour Window" && detail.QuotaBucketKind == WindowKind.Primary);
        Assert.Contains(usage.Details!, detail => detail.Name == "Weekly Quota" && detail.QuotaBucketKind == WindowKind.Secondary);
    }

    [Fact]
    public async Task GetUsageAsync_UsageQuotaOnly_UsesUsageWindowForTopLevelAsync()
    {
        // Arrange
        this._authService.Setup(s => s.GetCurrentToken()).Returns("test-key");

        var profileData = new { login = "user123" };
        var quotaData = new
        {
            copilot_plan = "copilot_individual",
            quota_snapshots = new
            {
                usage = new
                {
                    entitlement = 200.0,
                    remaining = 150.0,
                },
            },
        };

        this.SetupHttpResponse("https://api.github.com/user", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(profileData)),
        });

        this.SetupHttpResponse("https://api.github.com/copilot_internal/v2/token", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("{\"sku\":\"copilot_individual\"}"),
        });

        this.SetupHttpResponse("https://api.github.com/copilot_internal/user", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(quotaData)),
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = Assert.Single(result);
        Assert.Equal(75.0, usage.RequestsPercentage);
        Assert.Equal(50.0, usage.RequestsUsed);
        Assert.Equal(200.0, usage.RequestsAvailable);
        Assert.Contains("5-hour Window", usage.Description, StringComparison.Ordinal);
        Assert.NotNull(usage.Details);
        Assert.Contains(usage.Details!, detail => detail.Name == "5-hour Window");
        Assert.DoesNotContain(usage.Details!, detail => detail.Name == "Weekly Quota");
    }

    [Fact]
    public async Task GetUsageAsync_ConfigToken_InitializesAuthService_AndPreservesUsernameFallbackAsync()
    {
        // Arrange
        this._authService.Setup(s => s.GetCurrentToken()).Returns((string?)null);
        this._authService.Setup(s => s.GetUsernameAsync()).ReturnsAsync("octocat");

        this.SetupHttpResponse("https://api.github.com/user", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.Forbidden,
            Content = new StringContent("{\"message\":\"forbidden\"}"),
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.Equal("octocat", usage.AccountName);
        this._authService.Verify(s => s.InitializeToken("test-key"), Times.Once);
    }

    [Fact]
    public async Task GetUsageAsync_UsesDiscoveryToken_WhenConfigAndAuthServiceAreEmptyAsync()
    {
        // Arrange
        this.Config.ApiKey = string.Empty;
        this._authService.Setup(s => s.GetCurrentToken()).Returns((string?)null);
        this._authService.Setup(s => s.GetUsernameAsync()).ReturnsAsync("octocat");
        this._discoveryService
            .Setup(s => s.DiscoverAuthAsync(GitHubCopilotProvider.StaticDefinition))
            .ReturnsAsync(new ProviderAuthData("discovered-token"));

        this.SetupHttpResponse("https://api.github.com/user", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.Forbidden,
            Content = new StringContent("{\"message\":\"forbidden\"}"),
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.Equal("octocat", usage.AccountName);
        Assert.Equal("discovered-token", this.Config.ApiKey);
        this._authService.Verify(s => s.InitializeToken("discovered-token"), Times.Once);
    }

    [Fact]
    public async Task GetUsageAsync_ReturnsDiscoveredUsername_WhenTokenMissingAsync()
    {
        // Arrange
        this.Config.ApiKey = string.Empty;
        this._authService.Setup(s => s.GetCurrentToken()).Returns((string?)null);
        this._authService.Setup(s => s.GetUsernameAsync()).ReturnsAsync("rygel");
        this._discoveryService
            .Setup(s => s.DiscoverAuthAsync(GitHubCopilotProvider.StaticDefinition))
            .ReturnsAsync((ProviderAuthData?)null);

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Equal("rygel", usage.AccountName);
        Assert.Contains("Not authenticated", usage.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetUsageAsync_CurrentApiShape_UsesPercentRemainingAndPlanMappingAsync()
    {
        // Arrange
        this._authService.Setup(s => s.GetCurrentToken()).Returns("test-key");

        const string profileFixture = "{\"login\":\"snapshot-user\"}";
        var copilotUserFixture = LoadFixture("github_copilot_internal_user.snapshot.json");
        var token404Fixture = LoadFixture("github_copilot_internal_v2_token_404.snapshot.json");

        this.SetupHttpResponse("https://api.github.com/user", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(profileFixture),
        });

        // New API shape may not expose this endpoint for all users/plans.
        this.SetupHttpResponse("https://api.github.com/copilot_internal/v2/token", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.NotFound,
            Content = new StringContent(token404Fixture),
        });

        this.SetupHttpResponse("https://api.github.com/copilot_internal/user", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(copilotUserFixture),
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = Assert.Single(result);
        Assert.True(usage.IsAvailable);
        Assert.Equal("snapshot-user", usage.AccountName);
        Assert.Equal(50.6, usage.RequestsPercentage, 1);
        Assert.Equal(300.0, usage.RequestsAvailable);
        Assert.Equal(148.0, usage.RequestsUsed, 1);
        Assert.Equal("Copilot Individual", usage.AuthSource);

        Assert.NotNull(usage.Details);
        var weekly = Assert.Single(
            usage.Details!.Where(d => string.Equals(d.Name, "Weekly Quota", StringComparison.Ordinal)));
        Assert.Equal(WindowKind.Secondary, weekly.QuotaBucketKind);
        Assert.Contains("49% used", weekly.Used, StringComparison.Ordinal);
        Assert.Contains("152 / 300 remaining", weekly.Description, StringComparison.Ordinal);
    }
}
