// <copyright file="HttpProviderTestBase.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Infrastructure.Http;
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
        this.ResilientHttpClient = new Mock<IResilientHttpClient>();

        // Default behavior for SendAsync without policy: delegate to HttpClient
        this.ResilientHttpClient
            .Setup(x => x.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<CancellationToken>()))
            .Returns<HttpRequestMessage, CancellationToken>((req, ct) => this.HttpClient.SendAsync(req, ct));

        // Default behavior for SendAsync with policy: delegate to HttpClient (ignoring policy for tests)
        this.ResilientHttpClient
            .Setup(x => x.SendAsync(It.IsAny<HttpRequestMessage>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<HttpRequestMessage, string, CancellationToken>((req, policy, ct) => this.HttpClient.SendAsync(req, ct));
    }

    protected Mock<HttpMessageHandler> MessageHandler { get; }

    protected Mock<IResilientHttpClient> ResilientHttpClient { get; }

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
