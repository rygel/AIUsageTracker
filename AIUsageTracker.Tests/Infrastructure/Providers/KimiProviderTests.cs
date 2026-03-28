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
    private static readonly string TestApiKey = Guid.NewGuid().ToString();

    private readonly KimiProvider _provider;

    public KimiProviderTests()
    {
        this._provider = new KimiProvider(this.HttpClient, this.Logger.Object);
        this.Config.ApiKey = TestApiKey;
    }

    /// <summary>
    /// Tests basic usage calculation with simple values.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task GetUsageAsync_ValidResponse_CalculatesUsedPercentageCorrectlyAsync()
    {
        // Arrange - 10 used, 100 limit, 90 remaining
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
        Assert.Equal("Kimi for Coding", usage.ProviderName);

        // 10 used out of 100 = 10% used
        Assert.Equal(10, usage.UsedPercent);
        Assert.Equal(10, usage.RequestsUsed);
        Assert.Equal(100, usage.RequestsAvailable);
        Assert.True(usage.IsQuotaBased);
    }

    /// <summary>
    /// Tests usage calculation at 50% capacity.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task GetUsageAsync_HalfCapacity_CalculatesCorrectlyAsync()
    {
        // Arrange - 50 used, 100 limit, 50 remaining
        var responseContent = JsonSerializer.Serialize(new
        {
            usage = new { limit = 100, used = 50, remaining = 50 },
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

        // 50% used
        Assert.Equal(50, usage.UsedPercent);
        Assert.Equal(50, usage.RequestsUsed);
    }

    /// <summary>
    /// Tests usage calculation at near-capacity (high usage).
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task GetUsageAsync_NearCapacity_CalculatesCorrectlyAsync()
    {
        // Arrange - 95 used, 100 limit, 5 remaining
        var responseContent = JsonSerializer.Serialize(new
        {
            usage = new { limit = 100, used = 95, remaining = 5 },
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

        // 95% used
        Assert.Equal(95, usage.UsedPercent);
        Assert.Equal(95, usage.RequestsUsed);
    }

    /// <summary>
    /// Tests parsing of a realistic Kimi API response with 5h and 7d quotas.
    /// Response structure based on real API data (anonymized).
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task GetUsageAsync_RealisticResponse_WithDualQuotas_ParsesCorrectlyAsync()
    {
        // Arrange - Realistic response with both 5-hour and 7-day limits
        var fiveHourReset = DateTime.UtcNow.AddHours(3).ToString("o");
        var weeklyReset = DateTime.UtcNow.AddDays(5).ToString("o");

        var rawJson = $$"""
            {
                "usage": {
                    "limit": "10000",
                    "used": "2600",
                    "remaining": "7400",
                    "resetTime": "{{weeklyReset}}"
                },
                "limits": [
                    {
                        "window": { "duration": 300, "timeUnit": "TIME_UNIT_MINUTE" },
                        "detail": { "limit": "1000", "remaining": "740", "resetTime": "{{fiveHourReset}}" }
                    },
                    {
                        "window": { "duration": 7, "timeUnit": "TIME_UNIT_DAY" },
                        "detail": { "limit": "10000", "remaining": "7400", "resetTime": "{{weeklyReset}}" }
                    }
                ]
            }
            """;

        this.SetupHttpResponse("https://api.kimi.com/coding/v1/usages", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(rawJson),
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert — provider now emits flat cards: one per quota window
        var usages = result.ToList();
        // Should have 2 flat cards: 5h limit + 7d limit (Weekly-from-usage is skipped when a 7d entry exists in data.Limits)
        Assert.Equal(2, usages.Count);
        Assert.All(usages, u => Assert.Equal("kimi-for-coding", u.ProviderId)); // provider-id-guardrail-allow: test assertion
        Assert.All(usages, u => Assert.True(u.IsQuotaBased));
        Assert.All(usages, u => Assert.Equal(PlanType.Coding, u.PlanType));

        // Verify 5-hour burst card
        var burstCard = Assert.Single(usages, u => u.WindowKind == WindowKind.Burst);
        Assert.Equal("5h Limit", burstCard.Name);
        Assert.Equal(26, burstCard.UsedPercent, precision: 0); // (1000 - 740) / 1000 * 100 = 26% used
    }

    /// <summary>
    /// Tests parsing when user is hitting the 5-hour burst limit.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task GetUsageAsync_NearBurstLimit_ParsesCorrectlyAsync()
    {
        // Arrange - Near 5-hour limit but plenty of 7-day remaining
        var fiveHourReset = DateTime.UtcNow.AddMinutes(45).ToString("o");
        var weeklyReset = DateTime.UtcNow.AddDays(4).ToString("o");

        var rawJson = $$"""
            {
                "usage": {
                    "limit": "10000",
                    "used": "1500",
                    "remaining": "8500",
                    "resetTime": "{{weeklyReset}}"
                },
                "limits": [
                    {
                        "window": { "duration": 300, "timeUnit": "TIME_UNIT_MINUTE" },
                        "detail": { "limit": "1000", "remaining": "80", "resetTime": "{{fiveHourReset}}" }
                    }
                ]
            }
            """;

        this.SetupHttpResponse("https://api.kimi.com/coding/v1/usages", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(rawJson),
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert — provider emits flat cards (weekly from usage + burst from limits)
        var usages = result.ToList();

        // Weekly card from usage block (no 7d entry in limits)
        var weeklyCard = Assert.Single(usages, u => u.WindowKind == WindowKind.Rolling);
        Assert.Equal(15, weeklyCard.UsedPercent, precision: 0); // 1500 / 10000 = 15%

        // Burst card: 92% used (1000 - 80) / 1000 * 100
        var burstCard = Assert.Single(usages, u => u.WindowKind == WindowKind.Burst);
        Assert.Equal(92, burstCard.UsedPercent, precision: 0);

        // Next reset should be set on the burst card (sooner reset)
        Assert.NotNull(burstCard.NextResetTime);
    }

    /// <summary>
    /// Tests parsing when user is hitting the 7-day weekly limit.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task GetUsageAsync_NearWeeklyLimit_ParsesCorrectlyAsync()
    {
        // Arrange - High weekly usage, moderate burst usage
        var fiveHourReset = DateTime.UtcNow.AddHours(4).ToString("o");
        var weeklyReset = DateTime.UtcNow.AddDays(1).ToString("o");

        var rawJson = $$"""
            {
                "usage": {
                    "limit": "10000",
                    "used": "9200",
                    "remaining": "800",
                    "resetTime": "{{weeklyReset}}"
                },
                "limits": [
                    {
                        "window": { "duration": 300, "timeUnit": "TIME_UNIT_MINUTE" },
                        "detail": { "limit": "1000", "remaining": "650", "resetTime": "{{fiveHourReset}}" }
                    },
                    {
                        "window": { "duration": 7, "timeUnit": "TIME_UNIT_DAY" },
                        "detail": { "limit": "10000", "remaining": "800", "resetTime": "{{weeklyReset}}" }
                    }
                ]
            }
            """;

        this.SetupHttpResponse("https://api.kimi.com/coding/v1/usages", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(rawJson),
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert — provider emits flat cards (rolling + burst)
        var usages = result.ToList();

        // Rolling card from limits (7d entry exists in limits, so usage block is skipped)
        var rollingCards = usages.Where(u => u.WindowKind == WindowKind.Rolling).ToList();
        Assert.True(rollingCards.Count >= 1);
        var weeklyCard = rollingCards.FirstOrDefault(u => string.Equals(u.Name, "7d Limit", StringComparison.Ordinal));
        Assert.NotNull(weeklyCard);
        Assert.Equal(92, weeklyCard!.UsedPercent, precision: 0); // (10000 - 800) / 10000 * 100 = 92%
        Assert.Equal(9200, weeklyCard.RequestsUsed);
    }

    /// <summary>
    /// Tests parsing of fresh subscription with minimal usage.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task GetUsageAsync_MinimalUsage_ParsesCorrectlyAsync()
    {
        // Arrange - New user with minimal usage
        var weeklyReset = DateTime.UtcNow.AddDays(6).ToString("o");

        var rawJson = $$"""
            {
                "usage": {
                    "limit": "10000",
                    "used": "50",
                    "remaining": "9950",
                    "resetTime": "{{weeklyReset}}"
                },
                "limits": []
            }
            """;

        this.SetupHttpResponse("https://api.kimi.com/coding/v1/usages", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(rawJson),
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();

        // 0.5% used - rounds to 0 for display (50 / 10000 = 0.5%)
        Assert.True(usage.UsedPercent <= 1);
        Assert.Equal(50, usage.RequestsUsed);
        Assert.True(usage.IsAvailable);
    }

    /// <summary>
    /// Tests that the provider correctly handles numeric fields returned as JSON strings.
    /// The real Kimi API returns limit/used/remaining as strings (e.g., "100" not 100).
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task GetUsageAsync_WithStringTypedNumericFields_ParsesCorrectlyAsync()
    {
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
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(rawJson),
        });

        var result = await this._provider.GetUsageAsync(this.Config);

        // Provider now emits flat cards: burst (300min) + rolling (weekly from usage)
        var usages = result.ToList();
        var burstCard = Assert.Single(usages, u => u.WindowKind == WindowKind.Burst);
        var rollingCard = Assert.Single(usages, u => u.WindowKind == WindowKind.Rolling);

        // 26% used on the burst card (26 / 100)
        Assert.Equal(26, burstCard.UsedPercent, precision: 0);
        Assert.Equal(26, burstCard.RequestsUsed, precision: 0);
        Assert.Equal(100, burstCard.RequestsAvailable, precision: 0);
        Assert.Equal("5h Limit", burstCard.Name);
        Assert.Contains("remaining", burstCard.Description, StringComparison.Ordinal);
        Assert.Contains("remaining", rollingCard.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// Tests that hourly limits are correctly assigned as Primary window kind.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task GetUsageAsync_WithHourlyAndWeeklyLimits_SetsCorrectWindowKindsAsync()
    {
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

        // Assert — provider emits flat cards: Hourly (Burst) + Weekly from limits (Rolling)
        // Weekly-from-usage is skipped when a 7d entry exists in data.Limits
        var usages = result.ToList();
        Assert.Equal(2, usages.Count);

        var hourlyCard = Assert.Single(usages, u => u.WindowKind == WindowKind.Burst);
        var weeklyCards = usages.Where(u => u.WindowKind == WindowKind.Rolling).ToList();

        Assert.Equal(1, weeklyCards.Count); // Only the 7d limit from limits array
        Assert.Equal("Hourly Limit", hourlyCard.Name);

        // Verify description contains remaining count format
        Assert.Contains("remaining", hourlyCard.Description, StringComparison.Ordinal);
    }

    /// <summary>
    /// Tests that 3-hour windows are correctly assigned as Primary.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task GetUsageAsync_With3HourWindow_AssignsPrimaryWindowKindAsync()
    {
        var threeHourReset = DateTime.UtcNow.AddHours(2).ToString("o");

        var responseData = new
        {
            usage = new { limit = 5000, used = 1000, remaining = 4000 },
            limits = new object[]
            {
                new
                {
                    window = new { duration = 3, timeUnit = "TIME_UNIT_HOUR" },
                    detail = new { limit = 500, remaining = 300, resetTime = threeHourReset },
                },
            },
        };

        this.SetupHttpResponse("https://api.kimi.com/coding/v1/usages", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(responseData)),
        });

        var result = await this._provider.GetUsageAsync(this.Config);

        // Provider emits flat cards: burst (3h window) + rolling (weekly from usage)
        var burstCard = Assert.Single(result, u => u.WindowKind == WindowKind.Burst);
        Assert.Equal("3h Limit", burstCard.Name);
    }

    /// <summary>
    /// Tests that daily limits are correctly assigned as Primary.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
    [Fact]
    public async Task GetUsageAsync_WithDailyLimit_AssignsPrimaryWindowKindAsync()
    {
        var dailyReset = DateTime.UtcNow.AddHours(8).ToString("o");

        var responseData = new
        {
            usage = new { limit = 10000, used = 2000, remaining = 8000 },
            limits = new object[]
            {
                new
                {
                    window = new { duration = 1, timeUnit = "TIME_UNIT_DAY" },
                    detail = new { limit = 2000, remaining = 1500, resetTime = dailyReset },
                },
            },
        };

        this.SetupHttpResponse("https://api.kimi.com/coding/v1/usages", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(responseData)),
        });

        var result = await this._provider.GetUsageAsync(this.Config);

        // Provider emits flat cards: burst (1d window) + rolling (weekly from usage)
        var burstCard = Assert.Single(result, u => u.WindowKind == WindowKind.Burst);
        Assert.Equal("1d Limit", burstCard.Name); // Duration is formatted as "1d" not "Daily"
    }

    [Fact]
    public async Task GetUsageAsync_NoApiKey_ReturnsUnavailableAsync()
    {
        this.Config.ApiKey = string.Empty;

        var result = await this._provider.GetUsageAsync(this.Config);

        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Contains("API Key missing", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_Unauthorized_ReturnsUnavailableAsync()
    {
        this.SetupHttpResponse("https://api.kimi.com/coding/v1/usages", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.Unauthorized,
            Content = new StringContent("""{"error": "invalid_token"}"""),
        });

        var result = await this._provider.GetUsageAsync(this.Config);

        var usage = result.Single();
        Assert.False(usage.IsAvailable);
    }

    [Fact]
    public async Task GetUsageAsync_ServerError_ReturnsUnavailableAsync()
    {
        this.SetupHttpResponse("https://api.kimi.com/coding/v1/usages", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.InternalServerError,
            Content = new StringContent("""{"error": "internal_error"}"""),
        });

        var result = await this._provider.GetUsageAsync(this.Config);

        var usage = result.Single();
        Assert.False(usage.IsAvailable);
    }

    [Fact]
    public async Task GetUsageAsync_InvalidJson_ReturnsUnavailableAsync()
    {
        this.SetupHttpResponse("https://api.kimi.com/coding/v1/usages", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("not valid json"),
        });

        var result = await this._provider.GetUsageAsync(this.Config);

        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Contains("Failed to parse response", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_NullUsage_ReturnsUnavailableAsync()
    {
        this.SetupHttpResponse("https://api.kimi.com/coding/v1/usages", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("""{"limits": []}"""),
        });

        var result = await this._provider.GetUsageAsync(this.Config);

        var usage = result.Single();
        Assert.False(usage.IsAvailable);
    }

    [Fact]
    public void StaticDefinition_HasCorrectConfiguration()
    {
        var definition = KimiProvider.StaticDefinition;

        Assert.Equal("kimi-for-coding", definition.ProviderId); // provider-id-guardrail-allow: test assertion
        Assert.Equal("Kimi for Coding", definition.DisplayName);
        Assert.True(definition.IsQuotaBased);
        Assert.Equal("quota-based", definition.DefaultConfigType);
        Assert.Equal(PlanType.Coding, definition.PlanType);
        Assert.Contains("KIMI_API_KEY", definition.DiscoveryEnvironmentVariables);
        Assert.Contains("MOONSHOT_API_KEY", definition.DiscoveryEnvironmentVariables);
    }
}
