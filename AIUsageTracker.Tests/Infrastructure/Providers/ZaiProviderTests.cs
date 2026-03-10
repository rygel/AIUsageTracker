// <copyright file="ZaiProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Tests.Infrastructure;
using Moq;
using Moq.Protected;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class ZaiProviderTests : HttpProviderTestBase<ZaiProvider>
{
    private readonly ZaiProvider _provider;

    public ZaiProviderTests()
    {
        this._provider = new ZaiProvider(this.HttpClient, this.Logger.Object);
        this.Config.ApiKey = "test-key";
    }

    [Fact]
    public async Task GetUsageAsync_ValidResponse_CalculatesPercentageCorrectlyAsync()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            data = new
            {
                limits = new[]
                {
                    new
                    {
                        type = "TOKENS_LIMIT",
                        currentValue = 27000000L, // 20% used
                        usage = 135000000L, // Total limit
                        remaining = 108000000L,
                    },
                },
            },
        });

        this.SetupHttpResponse("https://api.z.ai/api/monitor/usage/quota/limit", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseContent),
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.Contains("80", usage.RequestsPercentage.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal); // 80% remaining
        Assert.Contains("80", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_NullTotalValue_ReturnsUnavailableAsync()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            data = new
            {
                limits = new[]
                {
                    new
                    {
                        type = "TOKENS_LIMIT",
                        currentValue = 1000000L,
                        usage = (long?)null,
                        remaining = (long?)null,
                    },
                },
            },
        });

        this.SetupHttpResponse("https://api.z.ai/api/monitor/usage/quota/limit", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseContent),
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Contains("Usage unknown", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_MultipleLimits_SelectsActiveLimitAsync()
    {
        // Arrange
        var responseContent = JsonSerializer.Serialize(new
        {
            data = new
            {
                limits = new object[]
                {
                    new
                    {
                        type = "TOKENS_LIMIT",
                        currentValue = 100000000L, // 100M Used -> 0 Remaining (Exhausted)
                        usage = 100000000L,
                        remaining = 0L,
                        nextResetTime = 1700000000L, // Past
                    },
                    new
                    {
                        type = "TOKENS_LIMIT",
                        currentValue = 0L, // 0 Used -> 100M Remaining (Active)
                        usage = 100000000L,
                        remaining = 100000000L,
                        nextResetTime = 4900000000L, // Future
                    },
                },
            },
        });

        this.SetupHttpResponse("https://api.z.ai/api/monitor/usage/quota/limit", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseContent),
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.Contains("100", usage.RequestsPercentage.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Contains("Remaining", usage.Description, StringComparison.Ordinal);
    }
}
