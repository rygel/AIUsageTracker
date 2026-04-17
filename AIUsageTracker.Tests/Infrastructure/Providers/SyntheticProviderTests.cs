// <copyright file="SyntheticProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class SyntheticProviderTests : HttpProviderTestBase<SyntheticProvider>
{
    private static readonly string TestApiKey = Guid.NewGuid().ToString();

    private readonly SyntheticProvider _provider;

    public SyntheticProviderTests()
    {
        this._provider = new SyntheticProvider(this.HttpClient, this.Logger.Object);
        this.Config.ApiKey = TestApiKey;
    }

    [Fact]
    public async Task GetUsageAsync_StandardPayload_ParsesCreditsCorrectlyAsync()
    {
        // Arrange
        var responseData = new
        {
            subscription = new
            {
                limit = 1000.0,
                usage = 250.0,
                resetAt = "2026-03-10T12:00:00Z",
            },
        };

        this.SetupHttpResponse("https://api.synthetic.new/v2/quotas", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(responseData)),
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.True(usage.IsAvailable);
        Assert.Equal("Synthetic.new", usage.ProviderName);
        Assert.Equal(25.0, usage.UsedPercent); // 250/1000 * 100 = 25% used
        Assert.Equal(250.0, usage.RequestsUsed);
        Assert.Contains("250 / 1000 credits", usage.Description, StringComparison.Ordinal);
        Assert.NotNull(usage.NextResetTime);
    }

    [Fact]
    public async Task GetUsageAsync_FlatPayload_ParsesSuccessfullyAsync()
    {
        // Arrange
        var responseData = new
        {
            total = 500.0,
            consumed = 100.0,
        };

        this.SetupHttpResponse("https://api.synthetic.new/v2/quotas", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(responseData)),
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.True(usage.IsAvailable);
        Assert.Equal(20.0, usage.UsedPercent); // 100/500 * 100 = 20% used
        Assert.Equal(100.0, usage.RequestsUsed);
    }

    [Fact]
    public async Task GetUsageAsync_NotFoundContent_ReturnsUnavailableAsync()
    {
        // Arrange
        this.SetupHttpResponse("https://api.synthetic.new/v2/quotas", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK, // API sometimes returns 200 with "Not Found" string
            Content = new StringContent("Not Found"),
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Contains("Not Found", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_EmptyJsonObject_ReturnsExpiredStateAsync()
    {
        // Arrange — API returns {} when subscription is expired/inactive
        this.SetupHttpResponse("https://api.synthetic.new/v2/quotas", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("{}"),
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Equal(ProviderUsageState.Expired, usage.State);
        Assert.Equal("No active subscription", usage.Description);
    }
}
