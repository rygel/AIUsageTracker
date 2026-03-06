using System.Net;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Tests.Infrastructure;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class OpenCodeProviderTests : HttpProviderTestBase<OpenCodeProvider>
{
    private readonly OpenCodeProvider _provider;

    public OpenCodeProviderTests()
    {
        _provider = new OpenCodeProvider(HttpClient, Logger.Object);
        Config.ApiKey = "test-opencode-key";
    }

    [Fact]
    public async Task GetUsageAsync_ValidResponse_ParsesCreditsCorrectly()
    {
        // Arrange
        var responseData = new
        {
            data = new
            {
                total_credits = 100.0,
                used_credits = 12.34
            }
        };

        SetupHttpResponse("https://api.opencode.ai/v1/credits", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(responseData))
        });

        // Act
        var result = await _provider.GetUsageAsync(Config);

        // Assert
        var usage = result.Single();
        Assert.True(usage.IsAvailable);
        Assert.Equal(OpenCodeProvider.StaticDefinition.DisplayName, usage.ProviderName);
        Assert.Equal(12.34, usage.RequestsUsed);
        Assert.Equal("USD", usage.UsageUnit);
        Assert.Equal("$12.34 used (7 days)", usage.Description);
    }

    [Fact]
    public async Task GetUsageAsync_ApiError_ReturnsUnavailable()
    {
        // Arrange
        SetupHttpResponse("https://api.opencode.ai/v1/credits", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.BadRequest,
            Content = new StringContent("{\"error\":\"bad_request\"}")
        });

        // Act
        var result = await _provider.GetUsageAsync(Config);

        // Assert
        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Equal(400, usage.HttpStatus);
        Assert.Contains("API Error", usage.Description);
    }
}
