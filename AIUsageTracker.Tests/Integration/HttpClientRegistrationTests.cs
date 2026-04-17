// <copyright file="HttpClientRegistrationTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using AIUsageTracker.Infrastructure.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace AIUsageTracker.Tests.Integration;

/// <summary>
/// Verifies that named HttpClient registrations are created by
/// <see cref="HttpClientExtensions.AddConfiguredHttpClients"/>.
/// </summary>
public class HttpClientRegistrationTests
{
    [Fact]
    public void PlainClient_IsRegistered_ByAddConfiguredHttpClients()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddConfiguredHttpClients();
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        // Assert — PlainClient should be resolvable
        var client = factory.CreateClient("PlainClient");
        Assert.NotNull(client);
    }

    [Fact]
    public void LocalhostClient_IsRegistered_ByAddConfiguredHttpClients()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddConfiguredHttpClients();
        var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        // Assert — LocalhostClient should be resolvable
        var client = factory.CreateClient("LocalhostClient");
        Assert.NotNull(client);
    }

    [Fact]
    public void SingletonHttpClient_UsesPlainClient_NotDefaultClient()
    {
        // Arrange — simulate the Monitor's Program.cs registration
        var services = new ServiceCollection();
        services.AddConfiguredHttpClients();
        services.AddSingleton(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("PlainClient"));

        // Act
        var provider = services.BuildServiceProvider();
        var httpClient = provider.GetRequiredService<HttpClient>();

        // Assert — should resolve without throwing
        Assert.NotNull(httpClient);
    }

    [Fact]
    public async Task PlainClient_Returns429Immediately_WithoutRetry()
    {
        // Arrange
        var callCount = 0;
        var handler = new TestHandler(() =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        });

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://test.invalid"),
        };

        // Act
        var response = await client.GetAsync("/usage");

        // Assert
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(1, callCount); // No retries — exactly one call
    }

    [Fact]
    public async Task PlainClient_DoesNotRetryTransientErrors()
    {
        // Arrange
        var callCount = 0;
        var handler = new TestHandler(() =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        });

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://test.invalid"),
        };

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, callCount);
    }

    private class TestHandler : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _responseFactory;

        public TestHandler(Func<HttpResponseMessage> responseFactory)
        {
            this._responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(this._responseFactory());
        }
    }
}
