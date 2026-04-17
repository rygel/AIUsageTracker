// <copyright file="XiaomiProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using System.Text.Json;
using AIUsageTracker.Infrastructure.Providers;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class XiaomiProviderTests : HttpProviderTestBase<XiaomiProvider>
{
    private static readonly string TestApiKey = Guid.NewGuid().ToString();

    private readonly XiaomiProvider _provider;

    public XiaomiProviderTests()
    {
        this._provider = new XiaomiProvider(this.HttpClient, this.Logger.Object);
        this.Config.ApiKey = TestApiKey;
    }

    [Fact]
    public async Task GetUsageAsync_ValidResponse_ParsesQuotaCorrectlyAsync()
    {
        // Arrange
        var responseData = new
        {
            code = 0,
            data = new
            {
                balance = 800.0,
                quota = 1000.0,
            },
        };

        this.SetupHttpResponse("https://api.xiaomimimo.com/v1/user/balance", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(JsonSerializer.Serialize(responseData)),
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.True(usage.IsAvailable);
        Assert.Contains("20", usage.UsedPercent.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal); // 200 used of 1000 = 20% used
        Assert.Equal(200.0, usage.RequestsUsed);
        Assert.Contains("800 remaining", usage.Description, StringComparison.Ordinal);
    }
}
