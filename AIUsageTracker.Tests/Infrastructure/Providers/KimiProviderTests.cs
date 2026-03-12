// <copyright file="KimiProviderTests.cs" company="AIUsageTracker">
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

public class KimiProviderTests : HttpProviderTestBase<KimiProvider>
{
    private readonly KimiProvider _provider;

    public KimiProviderTests()
    {
        this._provider = new KimiProvider(this.HttpClient, this.Logger.Object);
        this.Config.ApiKey = "test-key";
    }

    [Fact]
    public async Task GetUsageAsync_ValidResponse_CalculatesPercentageCorrectlyAsync()
    {
        // Arrange
        // 10 used, 100 limit, 90 remaining. RequestsPercentage uses remaining semantics.
        var responseContent = JsonSerializer.Serialize(new
        {
            usage = new { limit = 100, used = 10, remaining = 90 },
            limits = Array.Empty<object>(),
        });

        this.SetupHttpResponse("https://api.kimi.com/coding/v1/usages", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseContent),
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.Equal("Kimi", usage.ProviderName);
        Assert.Equal(90, usage.RequestsPercentage);
        Assert.Equal(10, usage.RequestsUsed);
        Assert.Equal(100, usage.RequestsAvailable);
        Assert.True(usage.IsQuotaBased);
    }

    [Fact]
    public async Task GetUsageAsync_WithLimitDetails_ParsesDetailsCorrectlyAsync()
    {
        // Arrange
        var resetTime = DateTime.UtcNow.AddHours(1).ToString("o"); // ISO 8601

        var responseData = new
        {
            usage = new { limit = 100, used = 50, remaining = 50 },
            limits = new object[]
            {
                new
                {
                    window = new { duration = 60, timeUnit = "TIME_UNIT_MINUTE" },
                    detail = new { limit = 1000, remaining = 500, resetTime = resetTime },
                },
            },
        };

        this.SetupHttpResponse("https://api.kimi.com/coding/v1/usages", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(responseData)),
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.NotNull(usage.Details);
        Assert.Equal(2, usage.Details.Count); // Weekly limit from usage + Hourly limit from limits array
        var detail = usage.Details.Last(); // Hourly limit is from limits array
        Assert.Equal("Hourly Limit", detail.Name);
        Assert.Contains("50.0%", detail.Used, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_WithHourlyAndWeeklyLimits_SetsCorrectWindowKindsAsync()
    {
        // Arrange
        var hourlyResetTime = DateTime.UtcNow.AddMinutes(30).ToString("o");
        var weeklyResetTime = DateTime.UtcNow.AddDays(7).ToString("o");

        var responseData = new
        {
            usage = new { limit = 100000, used = 25000, remaining = 75000 },
            limits = new object[]
            {
                new
                {
                    window = new { duration = 60, timeUnit = "TIME_UNIT_MINUTE" },
                    detail = new { limit = 3000, remaining = 1800, resetTime = hourlyResetTime },
                },
                new
                {
                    window = new { duration = 7, timeUnit = "TIME_UNIT_DAY" },
                    detail = new { limit = 100000, remaining = 75000, resetTime = weeklyResetTime },
                },
            },
        };

        this.SetupHttpResponse("https://api.kimi.com/coding/v1/usages", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(responseData)),
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.NotNull(usage.Details);
        Assert.Equal(3, usage.Details.Count); // Weekly limit from usage + Hourly + Weekly from limits array

        var hourlyDetail = usage.Details.FirstOrDefault(d => d.QuotaBucketKind == WindowKind.Primary);
        var weeklyDetailFromUsage = usage.Details.FirstOrDefault(d => d.QuotaBucketKind == WindowKind.Secondary && string.Equals(d.Name, "Weekly Limit", StringComparison.Ordinal));
        var weeklyDetailFromLimits = usage.Details.FirstOrDefault(d => d.QuotaBucketKind == WindowKind.Secondary && string.Equals(d.Name, "7d Limit", StringComparison.Ordinal));

        Assert.NotNull(hourlyDetail);
        Assert.NotNull(weeklyDetailFromUsage);
        Assert.NotNull(weeklyDetailFromLimits);
        Assert.Equal("Hourly Limit", hourlyDetail.Name);
        Assert.Equal("Weekly Limit", weeklyDetailFromUsage.Name);
        Assert.Equal("7d Limit", weeklyDetailFromLimits.Name);

        // Verify "used" percentage format for UI parsing
        Assert.Contains("% used", hourlyDetail.Used, StringComparison.Ordinal);
        Assert.Contains("% used", weeklyDetailFromUsage.Used, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_WithStringTypedNumericFields_ParsesCorrectlyAsync()
    {
        // The real Kimi API returns limit/used/remaining as JSON strings (e.g. "100"), not numbers.
        // This test ensures we handle both string and numeric JSON values correctly.
        var resetTime = DateTime.UtcNow.AddHours(5).ToString("o");
        var weeklyResetTime = DateTime.UtcNow.AddDays(4).ToString("o");

        // Raw JSON with string-typed numeric fields, exactly as the real API returns them
        var rawJson = $$"""
        {
          "usage": { "limit": "100", "used": "26", "remaining": "74", "resetTime": "{{weeklyResetTime}}" },
          "limits": [
            {
              "window": { "duration": 300, "timeUnit": "TIME_UNIT_MINUTE" },
              "detail": { "limit": "100", "remaining": "74", "resetTime": "{{resetTime}}" }
            }
          ]
        }
        """;

        this.SetupHttpResponse("https://api.kimi.com/coding/v1/usages", new HttpResponseMessage
        {
            StatusCode = System.Net.HttpStatusCode.OK,
            Content = new StringContent(rawJson),
        });

        var result = await this._provider.GetUsageAsync(this.Config);

        var usage = result.Single();
        Assert.Equal(74, usage.RequestsPercentage); // 74 remaining out of 100
        Assert.Equal(26, usage.RequestsUsed);
        Assert.Equal(100, usage.RequestsAvailable);
        Assert.NotNull(usage.Details);

        // Should have: Secondary (weekly from usage block) + Primary (300min window from limits)
        var primary = usage.Details!.FirstOrDefault(d => d.QuotaBucketKind == WindowKind.Primary);
        var secondary = usage.Details!.FirstOrDefault(d => d.QuotaBucketKind == WindowKind.Secondary);
        Assert.NotNull(primary);   // 300-minute window → Primary
        Assert.NotNull(secondary); // usage block → Secondary
        Assert.Equal("5h Limit", primary!.Name);
        Assert.Contains("% used", primary.Used, StringComparison.Ordinal);
        Assert.Contains("% used", secondary.Used, StringComparison.Ordinal);
    }
}
