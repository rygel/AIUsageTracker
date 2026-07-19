// <copyright file="AnthropicUsageProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class AnthropicUsageProviderTests : HttpProviderTestBase<AnthropicUsageProvider>
{
    private const string TestAdminKey = "sk-ant-admin01-test-key";
    private const string CostUrl = "https://api.anthropic.com/v1/organizations/cost_report";
    private const string UsageUrl = "https://api.anthropic.com/v1/organizations/usage_report/messages";

    private readonly AnthropicUsageProvider _provider;
    private readonly ProviderConfig _config;

    public AnthropicUsageProviderTests()
    {
        this._config = new ProviderConfig
        {
            ProviderId = "anthropic-usage",
            ApiKey = TestAdminKey,
        };
        this._provider = new AnthropicUsageProvider(this.HttpClient, NullLogger<AnthropicUsageProvider>.Instance);
    }

    [Fact]
    public async Task GetUsageAsync_NoApiKey_ReturnsMissing()
    {
        var config = new ProviderConfig { ProviderId = "anthropic-usage", ApiKey = string.Empty };

        var results = await this._provider.GetUsageAsync(config);

        var usage = Assert.Single(results);
        Assert.False(usage.IsAvailable);
        Assert.Equal(ProviderUsageState.Missing, usage.State);
        Assert.Contains("Admin API Key missing", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_CostReportError_ReturnsUnavailable()
    {
        var errorResponse = new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":{\"message\":\"invalid key\"}}"),
        };

        this.SetupHttpResponse(
            r => r.RequestUri != null && r.RequestUri.ToString().StartsWith(CostUrl, StringComparison.Ordinal),
            errorResponse);

        var results = await this._provider.GetUsageAsync(this._config);

        var usage = Assert.Single(results);
        Assert.False(usage.IsAvailable);
        Assert.Contains("Authentication failed", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_ValidCostAndUsage_ReturnsBothCards()
    {
        var costResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"data\":[{\"model\":\"claude-sonnet-4-20250514\",\"total_cost_usd\":5.42}]}"),
        };

        var usageResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"data\":[{\"model\":\"claude-sonnet-4-20250514\",\"input_tokens\":12000,\"output_tokens\":8000}]}"),
        };

        this.SetupHttpResponse(
            r => r.RequestUri != null && r.RequestUri.ToString().StartsWith(CostUrl, StringComparison.Ordinal),
            costResponse);
        this.SetupHttpResponse(
            r => r.RequestUri != null && r.RequestUri.ToString().StartsWith(UsageUrl, StringComparison.Ordinal),
            usageResponse);

        var results = (await this._provider.GetUsageAsync(this._config))
            .Cast<QuotaProviderUsage>()
            .ToList();

        Assert.Equal(2, results.Count);

        var costCard = results.FirstOrDefault(c => string.Equals(c.CardId, "daily-cost", StringComparison.Ordinal));
        Assert.NotNull(costCard);
        Assert.True(costCard!.IsAvailable);
        Assert.Contains("$5.42", costCard.Description, StringComparison.Ordinal);

        var tokenCard = results.FirstOrDefault(c => string.Equals(c.CardId, "daily-tokens", StringComparison.Ordinal));
        Assert.NotNull(tokenCard);
        Assert.True(tokenCard!.IsAvailable);
        Assert.Contains("20,000 tokens", tokenCard.Description, StringComparison.Ordinal);
        Assert.Contains("12,000 in", tokenCard.Description, StringComparison.Ordinal);
        Assert.Contains("8,000 out", tokenCard.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_NoCostData_ReturnsStatusCard()
    {
        var costResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\":[]}"),
        };

        var usageResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\":[]}"),
        };

        this.SetupHttpResponse(
            r => r.RequestUri != null && r.RequestUri.ToString().StartsWith(CostUrl, StringComparison.Ordinal),
            costResponse);
        this.SetupHttpResponse(
            r => r.RequestUri != null && r.RequestUri.ToString().StartsWith(UsageUrl, StringComparison.Ordinal),
            usageResponse);

        var results = await this._provider.GetUsageAsync(this._config);

        var usage = Assert.Single(results);
        Assert.True(usage.IsAvailable);
        Assert.Contains("no usage data", usage.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetUsageAsync_CostOnly_NoUsageTokens_ReturnsCostCardOnly()
    {
        var costResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"data\":[{\"model\":\"claude-sonnet-4-20250514\",\"total_cost_usd\":1.50}]}"),
        };

        var usageResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\":[]}"),
        };

        this.SetupHttpResponse(
            r => r.RequestUri != null && r.RequestUri.ToString().StartsWith(CostUrl, StringComparison.Ordinal),
            costResponse);
        this.SetupHttpResponse(
            r => r.RequestUri != null && r.RequestUri.ToString().StartsWith(UsageUrl, StringComparison.Ordinal),
            usageResponse);

        var results = (await this._provider.GetUsageAsync(this._config))
            .Cast<QuotaProviderUsage>()
            .ToList();

        var usage = Assert.Single(results);
        Assert.Equal("daily-cost", usage.CardId);
        Assert.Contains("$1.50", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticDefinition_HasCorrectProviderId()
    {
        Assert.Equal("anthropic-usage", AnthropicUsageProvider.StaticDefinition.ProviderId);
        Assert.Equal("Anthropic (Admin)", AnthropicUsageProvider.StaticDefinition.DisplayName);
        Assert.True(AnthropicUsageProvider.StaticDefinition.IsCurrencyUsage);
    }

    [Fact]
    public async Task GetUsageAsync_UsesXApiKeyHeaderNotBearer()
    {
        HttpRequestMessage? capturedRequest = null;

        var costResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\":[]}"),
        };

        var usageResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\":[]}"),
        };

        this.SetupHttpResponse(
            r =>
            {
                if (r.RequestUri != null && r.RequestUri.ToString().StartsWith(CostUrl, StringComparison.Ordinal))
                {
                    capturedRequest = r;
                    return true;
                }

                return false;
            },
            costResponse);
        this.SetupHttpResponse(
            r => r.RequestUri != null && r.RequestUri.ToString().StartsWith(UsageUrl, StringComparison.Ordinal),
            usageResponse);

        await this._provider.GetUsageAsync(this._config);

        Assert.NotNull(capturedRequest);
        Assert.Null(capturedRequest!.Headers.Authorization);
        Assert.True(capturedRequest.Headers.Contains("x-api-key"));
        Assert.True(capturedRequest.Headers.Contains("anthropic-version"));
    }
}
