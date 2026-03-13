// <copyright file="MistralProviderTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using AIUsageTracker.Core.Exceptions;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Providers;
using AIUsageTracker.Tests.Infrastructure;
using Moq;
using Moq.Protected;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure.Providers;

public class MistralProviderTests : HttpProviderTestBase<MistralProvider>
{
    private readonly MistralProvider _provider;

    public MistralProviderTests()
    {
        this._provider = new MistralProvider(this.ResilientHttpClient.Object, this.Logger.Object, new Mock<IProviderDiscoveryService>().Object);
        this.Config.ApiKey = "test-mistral-key";
    }

    [Fact]
    public async Task GetUsageAsync_ValidApiKey_ReturnsConnectedStatusAsync()
    {
        // Arrange
        this.SetupHttpResponse("https://api.mistral.ai/v1/models", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent("{\"data\":[]}"),
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.True(usage.IsAvailable);
        Assert.Equal("Mistral", usage.ProviderName);
        Assert.Equal("Connected (Check Dashboard)", usage.Description);
        Assert.Equal(200, usage.HttpStatus);
    }

    [Fact]
    public async Task GetUsageAsync_InvalidApiKey_UsesBaseClassErrorMappingAsync()
    {
        // Arrange
        this.SetupHttpResponse("https://api.mistral.ai/v1/models", new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.Unauthorized,
        });

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Equal(401, usage.HttpStatus);
        Assert.Contains("Authentication failed", usage.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUsageAsync_Timeout_UsesBaseClassExceptionMappingAsync()
    {
        // Arrange - Force a timeout by throwing TaskCanceledException
        this.SetupHttpResponse(_ => true, null!);
        this.MessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException());

        // Act
        var result = await this._provider.GetUsageAsync(this.Config);

        // Assert
        var usage = result.Single();
        Assert.False(usage.IsAvailable);
        Assert.Contains("timed out", usage.Description, StringComparison.OrdinalIgnoreCase);
    }
}
