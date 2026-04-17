// <copyright file="OpenCodeProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using System.Net.Http.Headers;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class OpenCodeProviderTests : HttpProviderTestBase<OpenCodeProvider>
{
    private const string CreditsUrl = "https://api.opencode.ai/v1/credits";

    private readonly OpenCodeProvider _provider;

    public OpenCodeProviderTests()
    {
        this._provider = new OpenCodeProvider(this.HttpClient, this.Logger.Object);
        this.Config.ApiKey = "sk-test-key";
    }

    private static StringContent JsonContent(string json)
    {
        var content = new StringContent(json, System.Text.Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    [Fact]
    public async Task GetUsageAsync_ValidResponse_ReturnsCreditsUsageAsync()
    {
        this.SetupHttpResponse(CreditsUrl, new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent("""
                {
                    "data": {
                        "total_credits": 100.0,
                        "used_credits": 23.5,
                        "remaining_credits": 76.5
                    }
                }
                """),
        });

        var result = await this._provider.GetUsageAsync(this.Config);
        var usage = result.Single();

        Assert.True(usage.IsAvailable);
        Assert.Equal(200, usage.HttpStatus);
        Assert.True(usage.IsQuotaBased);
        Assert.True(usage.DisplayAsFraction);
        Assert.Equal(23.5, usage.RequestsUsed);
        Assert.Equal(100.0, usage.RequestsAvailable);
        Assert.Equal(23.5, usage.UsedPercent, precision: 1);
        Assert.Contains("76.50 credits remaining", usage.Description, StringComparison.Ordinal);
        Assert.Contains("24% used", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_FullyUsed_Returns100PercentAsync()
    {
        this.SetupHttpResponse(CreditsUrl, new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent("""
                {
                    "data": {
                        "total_credits": 50.0,
                        "used_credits": 50.0,
                        "remaining_credits": 0.0
                    }
                }
                """),
        });

        var result = await this._provider.GetUsageAsync(this.Config);
        var usage = result.Single();

        Assert.True(usage.IsAvailable);
        Assert.Equal(100.0, usage.UsedPercent);
        Assert.Equal(50.0, usage.RequestsUsed);
        Assert.Contains("0.00 credits remaining", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_ZeroCredits_ReturnsZeroPercentAsync()
    {
        this.SetupHttpResponse(CreditsUrl, new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent("""
                {
                    "data": {
                        "total_credits": 0.0,
                        "used_credits": 0.0,
                        "remaining_credits": 0.0
                    }
                }
                """),
        });

        var result = await this._provider.GetUsageAsync(this.Config);
        var usage = result.Single();

        Assert.True(usage.IsAvailable);
        Assert.Equal(0.0, usage.UsedPercent);
    }

    [Fact]
    public async Task GetUsageAsync_MissingApiKey_ReturnsUnavailableAsync()
    {
        this.Config.ApiKey = string.Empty;

        var result = await this._provider.GetUsageAsync(this.Config);
        var usage = result.Single();

        Assert.False(usage.IsAvailable);
        Assert.Equal(ProviderUsageState.Missing, usage.State);
        Assert.Contains("API key missing", usage.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetUsageAsync_Unauthorized_ReturnsUnavailableAsync()
    {
        this.SetupHttpResponse(CreditsUrl, new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("Unauthorized"),
        });

        var result = await this._provider.GetUsageAsync(this.Config);
        var usage = result.Single();

        Assert.False(usage.IsAvailable);
        Assert.Equal(401, usage.HttpStatus);
    }

    [Fact]
    public async Task GetUsageAsync_NonJsonContentType_ReturnsEmptyAsync()
    {
        // API returns 200 with text/plain "Not Found" for unsupported accounts.
        // Provider detects non-JSON Content-Type and silently hides.
        this.SetupHttpResponse(CreditsUrl, new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("Not Found", System.Text.Encoding.UTF8, "text/plain"),
        });

        var result = await this._provider.GetUsageAsync(this.Config);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetUsageAsync_HtmlContentType_ReturnsEmptyAsync()
    {
        this.SetupHttpResponse(CreditsUrl, new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>Error</html>", System.Text.Encoding.UTF8, "text/html"),
        });

        var result = await this._provider.GetUsageAsync(this.Config);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetUsageAsync_MissingDataField_ReturnsUnavailableAsync()
    {
        this.SetupHttpResponse(CreditsUrl, new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent("{}"),
        });

        var result = await this._provider.GetUsageAsync(this.Config);
        var usage = result.Single();

        Assert.False(usage.IsAvailable);
        Assert.Contains("missing data", usage.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetUsageAsync_ComputesRemainingWhenNotProvidedAsync()
    {
        this.SetupHttpResponse(CreditsUrl, new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent("""
                {
                    "data": {
                        "total_credits": 200.0,
                        "used_credits": 75.0,
                        "remaining_credits": 0.0
                    }
                }
                """),
        });

        var result = await this._provider.GetUsageAsync(this.Config);
        var usage = result.Single();

        Assert.True(usage.IsAvailable);
        Assert.Contains("125.00 credits remaining", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticDefinition_HasCorrectMetadata()
    {
        var def = OpenCodeProvider.StaticDefinition;

        Assert.Equal("opencode-go", def.ProviderId);
        Assert.Equal("OpenCode Go", def.DisplayName);
        Assert.Equal(PlanType.Usage, def.PlanType);
        Assert.True(def.IsQuotaBased);
        Assert.Contains("OPENCODE_API_KEY", def.DiscoveryEnvironmentVariables);
        var schema = Assert.Single(def.SessionAuthFileSchemas);
        Assert.Equal("opencode", schema.RootProperty);
        Assert.Equal("key", schema.AccessTokenProperty);
    }
}
