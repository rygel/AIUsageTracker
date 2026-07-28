// <copyright file="ZaiProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

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
        var usage = result.OfType<QuotaProviderUsage>().Single();
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
        var usage = result.OfType<QuotaProviderUsage>().Single();
        Assert.False(usage.IsAvailable);
        Assert.Contains("Usage unknown", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_LegacyTwoTokenLimits_BothRenderAsCardsAsync()
    {
        // Regression for the old "select active limit" behaviour that picked one
        // of two TOKENS_LIMIT rows and silently dropped the other. As of 2026-07-27
        // Z.AI returns THREE limits (5h, Weekly, monthly tools); the provider must
        // render each distinct token window as its own card.
        // This test exercises a legacy 2-token-limit response (no unit/number)
        // where both rows lack unit metadata and should still both render.
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

        // Assert: both limit rows render as cards (Strategy A — never silently drop).
        var tokenCards = result.OfType<QuotaProviderUsage>()
            .Where(c => string.Equals(c.ProviderId, "zai-coding-plan", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(2, tokenCards.Count);
        Assert.Contains(tokenCards, c => c.UsedPercent >= 99); // 100M used
        Assert.Contains(tokenCards, c => c.UsedPercent < 1);   // fresh
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

        var usage = result.OfType<QuotaProviderUsage>().Single();
        Assert.True(usage.IsAvailable);
        Assert.Contains("5h window", usage.Description, StringComparison.Ordinal);

        // Must NOT contain a date string that looks like the billing period (many days away)
        Assert.DoesNotContain("Resets:", usage.Description, StringComparison.Ordinal);

        // nextResetTime should be null — no active window to point at
        Assert.Null(usage.NextResetTime);
    }

    [Fact]
    public async Task GetUsageAsync_BothTokenAndTimeLimits_ReturnsTwoCardsAsync()
    {
        // Regression: TIME_LIMIT (monthly search/reader/zread quota) must be its own card,
        // not merged into the TOKENS_LIMIT card via MCP adjustment.
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var futureMs = nowMs + 3600000L; // 1 hour from now

        var responseContent = JsonSerializer.Serialize(new
        {
            data = new
            {
                limits = new object[]
                {
                    new
                    {
                        type = "TIME_LIMIT",
                        unit = 5,
                        number = 1L,
                        usage = 1000L,      // total
                        currentValue = 117, // used
                        remaining = 883,
                        percentage = 11.0,
                        nextResetTime = nowMs + 2592000000L, // ~30 days
                        usageDetails = new[]
                        {
                            new { modelCode = "search-prime", usage = 82L },
                            new { modelCode = "web-reader", usage = 35L },
                            new { modelCode = "zread", usage = 0L },
                        },
                    },
                    new
                    {
                        type = "TOKENS_LIMIT",
                        percentage = 3.0,
                        unit = 3,
                        number = 5L,
                        nextResetTime = futureMs,
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

        // Must produce exactly 2 cards — one per limit type
        var cards = result.OfType<QuotaProviderUsage>().ToList();
        Assert.Equal(2, cards.Count);

        // Card 1: TOKENS_LIMIT (zai-coding-plan) — 3% used
        var tokenCard = cards.Single(c => string.Equals(c.ProviderId, "zai-coding-plan", StringComparison.Ordinal));
        Assert.True(tokenCard.IsAvailable);
        Assert.InRange(tokenCard.UsedPercent, 2.5, 3.5); // 3% used, 97% remaining
        Assert.Contains("Coding Plan", tokenCard.Description, StringComparison.Ordinal);

        // Card 2: TIME_LIMIT (zai) — 11% used
        var timeCard = cards.Single(c => string.Equals(c.ProviderId, "zai", StringComparison.Ordinal));
        Assert.True(timeCard.IsAvailable);
        Assert.InRange(timeCard.UsedPercent, 10.5, 11.5); // 11% used, 89% remaining
        Assert.Contains("Web Search & Reader", timeCard.Description, StringComparison.Ordinal);
        Assert.NotNull(timeCard.NextResetTime);
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

        var usage = result.OfType<QuotaProviderUsage>().Single();
        Assert.True(usage.IsAvailable);
        Assert.Contains("Resets:", usage.Description, StringComparison.Ordinal);
        Assert.NotNull(usage.NextResetTime);

        // Reset time should be roughly 3 hours from now, not days away
        var hoursUntilReset = (usage.NextResetTime!.Value - DateTime.Now).TotalHours;
        Assert.InRange(hoursUntilReset, 2.5, 3.5);
    }

    [Fact]
    public async Task GetUsageAsync_EmptyData_ReturnsSuccessfulWindowInactiveCardAsync()
    {
        // Z.AI returns HTTP 200 with `{"code":200,"msg":"Operation successful","data":{},"success":true}`
        // when the rolling 5h window has just rolled over and no usage has accrued yet.
        // This is a SUCCESSFUL upstream response — it must NOT be treated as a failure
        // (which would open the circuit breaker and surface as "Temporarily paused").
        var responseContent = JsonSerializer.Serialize(new
        {
            code = 200,
            msg = "Operation successful",
            data = new { },
            success = true,
        });

        this.SetupHttpResponse("https://api.z.ai/api/monitor/usage/quota/limit", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(responseContent),
        });

        var result = await this._provider.GetUsageAsync(this.Config);

        var usage = Assert.Single(result.OfType<QuotaProviderUsage>());
        Assert.True(
            usage.IsAvailable,
            "Empty `data:{}` is a successful upstream response — must keep circuit closed and surface as available.");
        Assert.Equal(200, usage.HttpStatus);
        Assert.Contains("window", usage.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetUsageAsync_LiveSchemaWithWeeklyLimit_Emits5hAndWeeklyTokenCardsAsync()
    {
        // Z.AI's live API (observed 2026-07-27) returns THREE limits:
        //   1. TOKENS_LIMIT unit=3 number=5  → 5-hour rolling (TOKENS_LIMIT)
        //   2. TOKENS_LIMIT unit=6 number=1  → 1-week rolling GLM weekly (NEW — was silently dropped)
        //   3. TIME_LIMIT  unit=5 number=1  → monthly tools/search/reader (TIME_LIMIT)
        //
        // The provider must emit BOTH token windows as separate cards.
        // The weekly card matters: when GLM weekly hits 100%, the user genuinely has
        // no quota left for the rest of the week — the UI must surface that.
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var weeklyResetMs = nowMs + 7L * 24 * 3600 * 1000;
        var monthlyResetMs = nowMs + 30L * 24 * 3600 * 1000;

        var responseContent = JsonSerializer.Serialize(new
        {
            data = new
            {
                limits = new object[]
                {
                    new
                    {
                        type = "TOKENS_LIMIT",
                        unit = 3,
                        number = 5L,
                        percentage = 0.0,
                    },
                    new
                    {
                        type = "TOKENS_LIMIT",
                        unit = 6,
                        number = 1L,
                        percentage = 100.0,
                        nextResetTime = weeklyResetMs,
                    },
                    new
                    {
                        type = "TIME_LIMIT",
                        unit = 5,
                        number = 1L,
                        usage = 1000L,
                        currentValue = 0L,
                        remaining = 1000L,
                        percentage = 0.0,
                        nextResetTime = monthlyResetMs,
                        usageDetails = new[]
                        {
                            new { modelCode = "search-prime", usage = 0L },
                            new { modelCode = "web-reader", usage = 0L },
                            new { modelCode = "zread", usage = 0L },
                        },
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
        var cards = result.OfType<QuotaProviderUsage>().ToList();

        // 2 token cards (zai-coding-plan: 5h + Weekly) + 1 time card (zai: monthly) = 3 total
        Assert.Equal(3, cards.Count);

        // 5h card: fresh window at 0% — its description must reflect the unit=3/number=5 mapping
        var fiveHour = cards.Single(c => string.Equals(c.ProviderId, "zai-coding-plan", StringComparison.Ordinal) && string.Equals(c.Name, "5h", StringComparison.Ordinal));
        Assert.Equal(0.0, fiveHour.UsedPercent, 1);
        Assert.Equal(WindowKind.Burst, fiveHour.WindowKind);
        Assert.Equal(TimeSpan.FromHours(5), fiveHour.PeriodDuration);
        Assert.Equal("5h", fiveHour.CardId);
        Assert.True(fiveHour.IsAvailable);

        // Weekly card: 100% used at the live API — must be surfaced, not silently dropped
        var weekly = cards.Single(c => string.Equals(c.ProviderId, "zai-coding-plan", StringComparison.Ordinal) && string.Equals(c.Name, "Weekly", StringComparison.Ordinal));
        Assert.Equal(100.0, weekly.UsedPercent, 1);
        Assert.Equal(WindowKind.Rolling, weekly.WindowKind);
        Assert.Equal(TimeSpan.FromDays(7), weekly.PeriodDuration);
        Assert.Equal("weekly", weekly.CardId);
        Assert.True(weekly.IsAvailable);
        Assert.NotNull(weekly.NextResetTime);
    }
}
