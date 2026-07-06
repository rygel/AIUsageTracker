// <copyright file="GroqProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class GroqProviderTests : HttpProviderTestBase<GroqProvider>
{
    private const string TestApiKey = "gsk_test123";
    private const string ModelsUrl = "https://api.groq.com/openai/v1/models";

    private readonly GroqProvider _provider;
    private readonly ProviderConfig _config;

    public GroqProviderTests()
    {
        _config = new ProviderConfig
        {
            ProviderId = "groq",
            ApiKey = TestApiKey,
        };
        this._provider = new GroqProvider(this.HttpClient, NullLogger<GroqProvider>.Instance);
    }

    [Fact]
    public async Task GetUsageAsync_NoApiKey_ReturnsMissing()
    {
        var config = new ProviderConfig { ProviderId = "groq", ApiKey = string.Empty };

        var results = await this._provider.GetUsageAsync(config);

        var usage = Assert.Single(results);
        Assert.False(usage.IsAvailable);
        Assert.Equal(ProviderUsageState.Missing, usage.State);
        Assert.Contains("API Key missing", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_ValidResponse_ParsesRateLimitHeaders()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\":[]}"),
        };
        response.Headers.Add("x-ratelimit-limit-requests", "14400");
        response.Headers.Add("x-ratelimit-remaining-requests", "14370");
        response.Headers.Add("x-ratelimit-reset-requests", "179.56");
        response.Headers.Add("x-ratelimit-limit-tokens", "18000");
        response.Headers.Add("x-ratelimit-remaining-tokens", "17997");
        response.Headers.Add("x-ratelimit-reset-tokens", "7.66");

        this.SetupHttpResponse(ModelsUrl, response);

        var results = await this._provider.GetUsageAsync(_config);
        var cards = results.ToList();

        Assert.Equal(2, cards.Count);
        Assert.All(cards, c => Assert.True(c.IsAvailable));
        Assert.All(cards, c => Assert.Equal("groq", c.ProviderId));
        Assert.All(cards, c => Assert.IsType<QuotaProviderUsage>(c));

        var dailyRequests = Assert.IsType<QuotaProviderUsage>(
            cards.First(c => c is QuotaProviderUsage q && q.CardId == "daily-requests"));
        Assert.Equal(14400, dailyRequests.RequestsAvailable);
        Assert.Equal(30, dailyRequests.RequestsUsed);
        Assert.True(dailyRequests.UsedPercent < 1);
        Assert.NotNull(dailyRequests.NextResetTime);

        var perMinuteTokens = Assert.IsType<QuotaProviderUsage>(
            cards.First(c => c is QuotaProviderUsage q && q.CardId == "per-minute-tokens"));
        Assert.Equal(18000, perMinuteTokens.RequestsAvailable);
        Assert.Equal(3, perMinuteTokens.RequestsUsed);
        Assert.NotNull(perMinuteTokens.NextResetTime);
    }

    [Fact]
    public async Task GetUsageAsync_NoRateLimitHeaders_ReturnsStatusOnlyCard()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\":[]}"),
        };

        this.SetupHttpResponse(ModelsUrl, response);

        var results = await this._provider.GetUsageAsync(_config);
        var usage = Assert.Single(results);

        Assert.True(usage.IsAvailable);
        Assert.IsType<StatusProviderUsage>(usage);
        Assert.Contains("Connected", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_Unauthorized_ReturnsUnavailable()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":{\"message\":\"Invalid API key\"}}"),
        };

        this.SetupHttpResponse(ModelsUrl, response);

        var results = await this._provider.GetUsageAsync(_config);
        var usage = Assert.Single(results);

        Assert.False(usage.IsAvailable);
        Assert.Contains("401", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_PartialHeaders_OnlyEmitsCardsForPresentHeaders()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\":[]}"),
        };
        response.Headers.Add("x-ratelimit-limit-requests", "1000");
        response.Headers.Add("x-ratelimit-remaining-requests", "900");
        response.Headers.Add("x-ratelimit-reset-requests", "3600");

        this.SetupHttpResponse(ModelsUrl, response);

        var results = await this._provider.GetUsageAsync(_config);
        var card = Assert.IsType<QuotaProviderUsage>(Assert.Single(results));

        Assert.Equal("daily-requests", card.CardId);
        Assert.Equal(1000, card.RequestsAvailable);
        Assert.Equal(100, card.RequestsUsed);
    }

    [Fact]
    public void StaticDefinition_HasExpectedProperties()
    {
        var definition = GroqProvider.StaticDefinition;

        Assert.Equal("groq", definition.ProviderId);
        Assert.Equal("Groq", definition.DisplayName);
        Assert.Equal(PlanType.Usage, definition.PlanType);
        Assert.False(definition.IsQuotaBased);
        Assert.Contains("GROQ_API_KEY", definition.DiscoveryEnvironmentVariables);
    }
}
