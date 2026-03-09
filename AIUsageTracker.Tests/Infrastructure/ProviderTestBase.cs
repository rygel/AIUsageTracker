using System.Reflection;
using AIUsageTracker.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure;

public abstract class ProviderTestBase<TProvider> where TProvider : class
{
    protected Mock<ILogger<TProvider>> Logger { get; }
`n    protected ProviderConfig Config { get; }
`n
    protected ProviderTestBase()
    {
        Logger = new Mock<ILogger<TProvider>>();
        var definition = GetProviderDefinition();
        Config = new ProviderConfig
        {
            ProviderId = definition?.ProviderId ?? GetProviderId(),
            PlanType = definition?.PlanType ?? PlanType.Usage,
            Type = definition?.DefaultConfigType ?? "pay-as-you-go"
        };
    }
`n
    protected static string GetProviderId()
    {
        var definition = GetProviderDefinition();
        if (definition != null)
        {
            return definition.ProviderId;
        }

        var providerTypeName = typeof(TProvider).Name;
        if (providerTypeName.EndsWith("Provider"))
        {
            providerTypeName = providerTypeName[..^8];
        }
        return providerTypeName.ToLowerInvariant().Replace(" ", "-");
    }
`n
    private static ProviderDefinition? GetProviderDefinition()
    {
        var property = typeof(TProvider).GetProperty(
            "StaticDefinition",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

        return property?.PropertyType == typeof(ProviderDefinition)
            ? property.GetValue(null) as ProviderDefinition
            : null;
    }
`n
    protected static string LoadFixture(string fileName)
    {
        var fixturePath = Path.Combine(AppContext.BaseDirectory, "TestData", "Providers", fileName);
        Assert.True(File.Exists(fixturePath), $"Fixture file not found: {fixturePath}");
        return File.ReadAllText(fixturePath);
    }
}
`n
public abstract class HttpProviderTestBase<TProvider> : ProviderTestBase<TProvider>
    where TProvider : class
{
    protected Mock<HttpMessageHandler> MessageHandler { get; }
`n    protected HttpClient HttpClient { get; }
`n
    protected HttpProviderTestBase()
    {
        MessageHandler = new Mock<HttpMessageHandler>();
        HttpClient = new HttpClient(MessageHandler.Object);
    }
`n
    protected void SetupHttpResponse(string url, HttpResponseMessage response)
    {
        MessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri != null && r.RequestUri.ToString() == url),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }
`n
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
