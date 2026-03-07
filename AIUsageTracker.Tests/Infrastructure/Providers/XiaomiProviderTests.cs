using System.Net;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Tests.Infrastructure;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class XiaomiProviderTests : HttpProviderTestBase<XiaomiProvider>
{
    private readonly XiaomiProvider _provider;

    public XiaomiProviderTests()
    {
        _provider = new XiaomiProvider(HttpClient, Logger.Object);
        Config.ApiKey = "test-key";
    }

    [Fact]
    public async Task GetUsageAsync_ValidResponse_ParsesQuotaCorrectly()
    {
        // Arrange
        var responseData = new
        {
            code = 0,
            data = new
            {
                balance = 800.0,
                quota = 1000.0
            }
        };

        SetupHttpResponse("https://api.xiaomimimo.com/v1/user/balance", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(responseData))
        });

        // Act
        var result = await _provider.GetUsageAsync(Config);

        // Assert
        var usage = result.Single();
        Assert.True(usage.IsAvailable);
        Assert.Contains("80", usage.RequestsPercentage.ToString(System.Globalization.CultureInfo.InvariantCulture)); // Handle culture
        Assert.Equal(200.0, usage.RequestsUsed);
        Assert.Contains("800 remaining", usage.Description);
    }
}
