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
    private static readonly string TestApiKey = Guid.NewGuid().ToString();

    private readonly GitHubCopilotProvider _provider;
    private readonly Mock<IGitHubAuthService> _authService;
    private readonly Mock<IProviderDiscoveryService> _discoveryService;

    public GitHubCopilotProviderTests()
    {
        this._authService = new Mock<IGitHubAuthService>();
        this._discoveryService = new Mock<IProviderDiscoveryService>();
        this._provider = new GitHubCopilotProvider(this.HttpClient, this.Logger.Object, this._authService.Object, this._discoveryService.Object);
        this.Config.ApiKey = TestApiKey;
    }

    [Fact]
    public async Task GetUsageAsync_ValidResponse_ParsesQuotaCorrectlyAsync()
    {
        // Arrange
        this._authService.Setup(s => s.GetCurrentToken()).Returns(TestApiKey);

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
        Assert.Equal(30.0, usage.UsedPercent); // 70 remaining out of 100 = 30% used
        Assert.Contains("Copilot Individual", usage.AuthSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_BothQuotaWindows_PrefersWeeklyPremiumWindowForTopLevelAsync()
    {
        // Arrange
        this._authService.Setup(s => s.GetCurrentToken()).Returns(TestApiKey);

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

        // Assert — provider now emits flat cards: one per quota window
        var usages = result.ToList();
        Assert.Equal(2, usages.Count);
        Assert.All(usages, u => Assert.True(u.IsAvailable));

        var weeklyCard = Assert.Single(usages, u => u.WindowKind == WindowKind.Rolling);
        Assert.Equal(80.0, weeklyCard.UsedPercent); // 20 remaining out of 100 = 80% used
        Assert.Equal(80.0, weeklyCard.RequestsUsed);
        Assert.Equal(100.0, weeklyCard.RequestsAvailable);
        Assert.Contains("Weekly Quota", weeklyCard.Name ?? string.Empty, StringComparison.Ordinal);
        Assert.Contains("remaining", weeklyCard.Description, StringComparison.OrdinalIgnoreCase);

        var burstCard = Assert.Single(usages, u => u.WindowKind == WindowKind.Burst);
        Assert.Contains("5-Hour Window", burstCard.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetUsageAsync_UsageQuotaOnly_UsesUsageWindowForTopLevelAsync()
    {
        // Arrange
        this._authService.Setup(s => s.GetCurrentToken()).Returns(TestApiKey);

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

        // Assert — provider emits only a burst card (no weekly quota in this scenario)
        var usage = Assert.Single(result);
        Assert.Equal(25.0, usage.UsedPercent); // 150 remaining out of 200 = 25% used
        Assert.Equal(50.0, usage.RequestsUsed);
        Assert.Equal(200.0, usage.RequestsAvailable);
        Assert.Contains("5-Hour Window", usage.Name ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("remaining", usage.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(WindowKind.Burst, usage.WindowKind);
        Assert.DoesNotContain(result, u => u.WindowKind == WindowKind.Rolling);
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
        this._authService.Verify(s => s.InitializeToken(TestApiKey), Times.Once);
    }

    [Fact]
    public async Task GetUsageAsync_UsesDiscoveryToken_WhenConfigAndAuthServiceAreEmptyAsync()
    {
        // Arrange
        this.Config.ApiKey = string.Empty;
        this._authService.Setup(s => s.GetCurrentToken()).Returns((string?)null);
        this._authService.Setup(s => s.GetUsernameAsync()).ReturnsAsync("octocat");
        this._discoveryService
            .Setup(s => s.DiscoverAuthAsync(GitHubCopilotProvider.StaticDefinition.CreateAuthDiscoverySpec()))
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
            .Setup(s => s.DiscoverAuthAsync(GitHubCopilotProvider.StaticDefinition.CreateAuthDiscoverySpec()))
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
        this._authService.Setup(s => s.GetCurrentToken()).Returns(TestApiKey);

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
        Assert.Equal(49.4, usage.UsedPercent, 1); // percent_remaining=50.6, so UsedPercent = 100 - 50.6 = 49.4
        Assert.Equal(300.0, usage.RequestsAvailable);
        Assert.Equal(148.0, usage.RequestsUsed, 1);
        Assert.Equal("Copilot Individual", usage.AuthSource);

        // Provider now emits flat cards — the weekly card is a top-level ProviderUsage
        Assert.Equal(WindowKind.Rolling, usage.WindowKind);
        Assert.Contains("152 / 300 remaining", usage.Description, StringComparison.Ordinal);
    }
}
