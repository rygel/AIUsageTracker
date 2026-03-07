using System.Net;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Tests.Infrastructure;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class MinimaxProviderTests : HttpProviderTestBase<MinimaxProvider>
{
    private readonly MinimaxProvider _provider;

    public MinimaxProviderTests()
    {
        _provider = new MinimaxProvider(HttpClient, Logger.Object);
        Config.ApiKey = "test-key";
    }

    [Fact]
    public async Task GetUsageAsync_ValidResponse_ParsesUsageCorrectly()
    {
        // Arrange
        var responseData = new
        {
            usage = new
            {
                tokens_used = 30.0,
                tokens_limit = 100.0
            }
        };

        SetupHttpResponse("https://api.minimax.chat/v1/user/usage", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(responseData))
        });

        // Act
        var result = await _provider.GetUsageAsync(Config);

        // Assert
        var usage = result.Single();
        Assert.True(usage.IsAvailable);
        Assert.Contains("30", usage.RequestsUsed.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains("100", usage.RequestsAvailable.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
