// <copyright file="OpenRouterProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Tests.Infrastructure;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class OpenRouterProviderTests : HttpProviderTestBase<OpenRouterProvider>
{
    private static readonly string TestApiKey = Guid.NewGuid().ToString();

    private readonly OpenRouterProvider _provider;

    public OpenRouterProviderTests()
    {
        this._provider = new OpenRouterProvider(this.HttpClient, this.Logger.Object);
        this.Config.ApiKey = TestApiKey;
    }

    [Fact]
    public async Task GetUsageAsync_ValidResponse_ParsesCreditsAndKeyInfoAsync()
    {
        var creditsResponse = new
        {
            data = new
            {
                total_credits = 10.0,
                total_usage = 2.5,
            },
        };

        var keyResponse = new
        {
            data = new
            {
                label = "My Project Key",
                limit = 100.0,
                is_free_tier = false,
            },
        };

        this.SetupHttpResponse("https://openrouter.ai/api/v1/credits", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(creditsResponse)),
        });

        this.SetupHttpResponse("https://openrouter.ai/api/v1/key", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(keyResponse)),
        });

        var result = await this._provider.GetUsageAsync(this.Config);

        var usage = result.Single();
        Assert.True(usage.IsAvailable);
        Assert.Equal("My Project Key", usage.ProviderName);
        Assert.Equal(25.0, usage.UsedPercent); // 2.5 used of 10 total = 25% used
        Assert.Equal(2.5, usage.RequestsUsed);

        // UsageUnit removed; OpenRouter does not set IsCurrencyUsage since it uses Credits not USD
        Assert.Equal("7.50 Credits Remaining", usage.Description);

        Assert.Contains(
            usage.Details!,
            detail => string.Equals(detail.Name, "Spending Limit", StringComparison.Ordinal) &&
                detail.Description.StartsWith("100.00", StringComparison.Ordinal));
        Assert.Contains(
            usage.Details!,
            detail => string.Equals(detail.Name, "Free Tier", StringComparison.Ordinal) &&
                string.Equals(detail.Description, "No", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetUsageAsync_CreditsApiError_ReturnsUnavailableAsync()
    {
        this.SetupHttpResponse("https://openrouter.ai/api/v1/credits", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.Unauthorized,
        });

        var result = await this._provider.GetUsageAsync(this.Config);

        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Equal(401, usage.HttpStatus);
        Assert.Contains("Authentication failed", usage.Description, StringComparison.Ordinal);
    }
}
