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

    public GitHubCopilotProviderTests()
    {
        this._authService = new Mock<IGitHubAuthService>();
        this._provider = new GitHubCopilotProvider(this.ResilientHttpClient.Object, this.Logger.Object, this._authService.Object, new Mock<IProviderDiscoveryService>().Object);
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
}
