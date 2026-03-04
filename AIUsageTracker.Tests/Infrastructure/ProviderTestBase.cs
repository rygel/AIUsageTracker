using AIUsageTracker.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure;

public abstract class ProviderTestBase<TProvider> where TProvider : class
{
    protected Mock<ILogger<TProvider>> Logger { get; }
    protected ProviderConfig Config { get; }

    protected ProviderTestBase()
    {
        Logger = new Mock<ILogger<TProvider>>();
        Config = new ProviderConfig { ProviderId = GetProviderId() };
    }

    protected static string GetProviderId()
    {
        var providerTypeName = typeof(TProvider).Name;
        if (providerTypeName.EndsWith("Provider"))
        {
            providerTypeName = providerTypeName[..^8];
        }
        return providerTypeName.ToLowerInvariant().Replace(" ", "-");
    }

    protected static string LoadFixture(string fileName)
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "TestData", "Providers", fileName);
        Assert.True(File.Exists(fixturePath), $"Fixture file not found: {fixturePath}");
        return File.ReadAllText(fixturePath);
    }
}

public abstract class HttpProviderTestBase<TProvider> : ProviderTestBase<TProvider>
    where TProvider : class
{
    protected Mock<HttpMessageHandler> MessageHandler { get; }
    protected HttpClient HttpClient { get; }

    protected HttpProviderTestBase()
    {
        MessageHandler = new Mock<HttpMessageHandler>();
        HttpClient = new HttpClient(MessageHandler.Object);
    }

    protected void SetupHttpResponse(string url, HttpResponseMessage response)
    {
        MessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri != null && r.RequestUri.ToString() == url),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }

    protected void SetupHttpResponse(Func<HttpRequestMessage, bool> requestMatcher, HttpResponseMessage response)
    {
        MessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => requestMatcher(r)),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }
}
