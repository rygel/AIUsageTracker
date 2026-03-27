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
    private static readonly string TestApiKey = Guid.NewGuid().ToString();

    private readonly ZaiProvider _provider;

    public ZaiProviderTests()
    {
        this._provider = new ZaiProvider(this.HttpClient, this.Logger.Object);
        this.Config.ApiKey = TestApiKey;
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
        Assert.Equal("Z.ai Coding Plan", usage.ProviderName);
        Assert.Contains("20", usage.UsedPercent.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal); // 20% used (80% remaining)
        Assert.Contains("80", usage.Description, StringComparison.Ordinal);
        Assert.Contains("Coding Plan", usage.Description, StringComparison.Ordinal);
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

        // Active limit has 100M remaining = 0% used; description should show 100% remaining
        Assert.Equal(0, usage.UsedPercent, 1); // 0% used (100% remaining)
        Assert.Contains("Remaining", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_FreshTokenLimit_ShowsWindowLabelNotBillingPeriodDateAsync()
    {
        // Regression: when percentage=0 (fresh/unused), the API returns the billing period end
        // date as nextResetTime (e.g. Mar 23), not the 5h rolling window close. The fix uses
        // unit/number to show "5h window" label instead of a misleading 7-day countdown.
        var billingPeriodEnd = DateTimeOffset.UtcNow.AddDays(8).ToUnixTimeMilliseconds();

        var responseContent = JsonSerializer.Serialize(new
        {
            data = new
            {
                limits = new object[]
                {
                    new
                    {
                        type = "TOKENS_LIMIT",
                        percentage = 0.0,
                        unit = 3,       // hours
                        number = 5L,    // 5-hour rolling window
                        nextResetTime = billingPeriodEnd,
                    },
                },
            },
        });

        this.SetupHttpResponse("https://api.z.ai/api/monitor/usage/quota/limit", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseContent),
        });

        var result = await this._provider.GetUsageAsync(this.Config);

        var usage = result.Single();
        Assert.True(usage.IsAvailable);
        Assert.Contains("5h window", usage.Description, StringComparison.Ordinal);

        // Must NOT contain a date string that looks like the billing period (many days away)
        Assert.DoesNotContain("Resets:", usage.Description, StringComparison.Ordinal);

        // nextResetTime should be null — no active window to point at
        Assert.Null(usage.NextResetTime);
    }

    [Fact]
    public async Task GetUsageAsync_ActiveTokenLimit_ShowsActualWindowResetTimeAsync()
    {
        // When percentage > 0 the API returns the current 5h window's close time as nextResetTime.
        // The fix must use that timestamp directly rather than falling back to the billing period end.
        var windowCloseMs = DateTimeOffset.UtcNow.AddHours(3).ToUnixTimeMilliseconds();

        var responseContent = JsonSerializer.Serialize(new
        {
            data = new
            {
                limits = new object[]
                {
                    new
                    {
                        type = "TOKENS_LIMIT",
                        percentage = 29.0,
                        unit = 3,
                        number = 5L,
                        nextResetTime = windowCloseMs,
                    },
                },
            },
        });

        this.SetupHttpResponse("https://api.z.ai/api/monitor/usage/quota/limit", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseContent),
        });

        var result = await this._provider.GetUsageAsync(this.Config);

        var usage = result.Single();
        Assert.True(usage.IsAvailable);
        Assert.Contains("Resets:", usage.Description, StringComparison.Ordinal);
        Assert.NotNull(usage.NextResetTime);

        // Reset time should be roughly 3 hours from now, not days away
        var hoursUntilReset = (usage.NextResetTime!.Value - DateTime.Now).TotalHours;
        Assert.InRange(hoursUntilReset, 2.5, 3.5);
    }
}
