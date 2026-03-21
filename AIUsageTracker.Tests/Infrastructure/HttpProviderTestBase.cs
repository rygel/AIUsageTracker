// <copyright file="HttpProviderTestBase.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using Moq;
using Moq.Protected;

namespace AIUsageTracker.Tests.Infrastructure;

public abstract class HttpProviderTestBase<TProvider> : ProviderTestBase<TProvider>
    where TProvider : class
{
    protected HttpProviderTestBase()
    {
        this.MessageHandler = new Mock<HttpMessageHandler>();
        this.HttpClient = new HttpClient(this.MessageHandler.Object);
    }

    protected Mock<HttpMessageHandler> MessageHandler { get; }

    protected HttpClient HttpClient { get; }

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
