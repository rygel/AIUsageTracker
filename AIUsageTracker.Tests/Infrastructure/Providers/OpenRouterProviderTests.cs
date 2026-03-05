using System.Net;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Tests.Infrastructure;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class OpenRouterProviderTests : HttpProviderTestBase<OpenRouterProvider>
{
    private readonly OpenRouterProvider _provider;

    public OpenRouterProviderTests()
    {
        _provider = new OpenRouterProvider(HttpClient, Logger.Object);
        Config.ApiKey = "test-openrouter-key";
    }

    [Fact]
    public async Task GetUsageAsync_ValidResponse_ParsesCreditsAndKeyInfo()
    {
        // Arrange
        var creditsResponse = new
        {
            data = new
            {
                total_credits = 10.0,
                total_usage = 2.5
            }
        };

        var keyResponse = new
        {
            data = new
            {
                label = "My Project Key",
                limit = 100.0,
                is_free_tier = false
            }
        };

        SetupHttpResponse("https://openrouter.ai/api/v1/credits", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(creditsResponse))
        });

        SetupHttpResponse("https://openrouter.ai/api/v1/key", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(keyResponse))
        });

        // Act
        var result = await _provider.GetUsageAsync(Config);

        // Assert
        var usage = result.Single();
        Assert.True(usage.IsAvailable);
        Assert.Equal("My Project Key", usage.ProviderName);
        Assert.Equal(75.0, usage.RequestsPercentage); // (10-2.5)/10 * 100
        Assert.Equal(2.5, usage.RequestsUsed);
        Assert.Equal("Credits", usage.UsageUnit);
        Assert.Equal("7.50 Credits Remaining", usage.Description);
        
        Assert.Contains(usage.Details!, d => d.Name == "Spending Limit" && d.Description.StartsWith("100.00"));
        Assert.Contains(usage.Details!, d => d.Name == "Free Tier" && d.Description == "No");
    }

    [Fact]
    public async Task GetUsageAsync_CreditsApiError_ReturnsUnavailable()
    {
        // Arrange
        SetupHttpResponse("https://openrouter.ai/api/v1/credits", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.Unauthorized
        });

        // Act
        var result = await _provider.GetUsageAsync(Config);

        // Assert
        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Equal(401, usage.HttpStatus);
        Assert.Contains("Authentication failed", usage.Description);
    }
}
