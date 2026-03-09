// <copyright file="ProviderTestBase.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Reflection;
using AIUsageTracker.Core.Models;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure;

public abstract class ProviderTestBase<TProvider>
    where TProvider : class
{
    protected Mock<ILogger<TProvider>> Logger { get; }

    protected ProviderConfig Config { get; }

    protected ProviderTestBase()
    {
        this.Logger = new Mock<ILogger<TProvider>>();
        var definition = GetProviderDefinition();
        this.Config = new ProviderConfig
        {
            ProviderId = definition?.ProviderId ?? GetProviderId(),
            PlanType = definition?.PlanType ?? PlanType.Usage,
            Type = definition?.DefaultConfigType ?? "pay-as-you-go",
        };
    }

    protected static string GetProviderId()
    {
        var definition = GetProviderDefinition();
        if (definition != null)
        {
            return definition.ProviderId;
        }

        var providerTypeName = typeof(TProvider).Name;
        if (providerTypeName.EndsWith("Provider", StringComparison.Ordinal))
        {
            providerTypeName = providerTypeName[..^8];
        }

        return providerTypeName.ToLowerInvariant().Replace(" ", "-");
    }

    private static ProviderDefinition? GetProviderDefinition()
    {
        var property = typeof(TProvider).GetProperty(
            "StaticDefinition",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);

        return property?.PropertyType == typeof(ProviderDefinition)
            ? property.GetValue(null) as ProviderDefinition
            : null;
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
        this.MessageHandler = new Mock<HttpMessageHandler>();
        this.HttpClient = new HttpClient(this.MessageHandler.Object);
    }

    protected void SetupHttpResponse(string url, HttpResponseMessage response)
    {
        this.MessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.RequestUri != null && r.RequestUri.ToString() == url),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }

    protected void SetupHttpResponse(Func<HttpRequestMessage, bool> requestMatcher, HttpResponseMessage response)
    {
        this.MessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => requestMatcher(r)),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }
}
