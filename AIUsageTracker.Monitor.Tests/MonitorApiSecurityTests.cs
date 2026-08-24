// <copyright file="MonitorApiSecurityTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System;
using System.Net;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.MonitorClient;
using AIUsageTracker.Monitor.Security;
using Microsoft.AspNetCore.Http;

namespace AIUsageTracker.Monitor.Tests;

public sealed class MonitorApiSecurityTests
{
    private const string AccessToken = "monitor-test-token-that-is-long-enough-for-tests";

    [Fact]
    public async Task AuthenticationMiddleware_AllowsPublicHealthWithoutTokenAsync()
    {
        var nextCalled = false;
        var middleware = new MonitorApiAuthenticationMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            AccessToken);
        var context = new DefaultHttpContext();
        context.Request.Path = MonitorApiRoutes.Health;

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Equal((int)HttpStatusCode.OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task AuthenticationMiddleware_RejectsProtectedApiWithoutTokenAsync()
    {
        var nextCalled = false;
        var middleware = new MonitorApiAuthenticationMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            AccessToken);
        var context = new DefaultHttpContext();
        context.Request.Path = MonitorApiRoutes.Config;

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal((int)HttpStatusCode.Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task AuthenticationMiddleware_AllowsProtectedApiWithBearerTokenAsync()
    {
        var nextCalled = false;
        var middleware = new MonitorApiAuthenticationMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            AccessToken);
        var context = new DefaultHttpContext();
        context.Request.Path = MonitorApiRoutes.Config;
        context.Request.Headers.Authorization = $"Bearer {AccessToken}";

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Equal((int)HttpStatusCode.OK, context.Response.StatusCode);
    }

    [Fact]
    public void ProviderConfigResponse_RedactsKeyAndPreservesCredentialState()
    {
        var source = new ProviderConfig
        {
            ProviderId = "openai",
            ApiKey = "secret-session-token",
            AuthSource = "manual",
        };

        var response = ProviderConfigResponse.FromProviderConfig(source);
        var json = JsonSerializer.Serialize(response);
        var clientConfig = response.ToProviderConfig();

        Assert.DoesNotContain(source.ApiKey, json, StringComparison.Ordinal);
        Assert.True(response.HasKey);
        Assert.True(response.IsSessionToken);
        Assert.Empty(clientConfig.ApiKey);
        Assert.True(clientConfig.HasStoredApiKey);
        Assert.True(clientConfig.HasStoredSessionToken);
    }

    [Fact]
    public void ProviderConfigUpdateRequest_PreservesExistingKeyOnRedactedRoundTrip()
    {
        var clientConfig = new ProviderConfig
        {
            ProviderId = "openai",
            HasStoredApiKey = true,
            HasStoredSessionToken = true,
            ShowInTray = true,
        };
        var existing = new ProviderConfig
        {
            ProviderId = "openai",
            ApiKey = "existing-secret",
            ShowInTray = false,
        };

        var request = ProviderConfigUpdateRequest.FromProviderConfig(clientConfig);
        var merged = request.ToProviderConfig(existing);

        Assert.True(request.PreserveApiKey);
        Assert.Equal("existing-secret", merged.ApiKey);
        Assert.True(merged.ShowInTray);
    }

    [Fact]
    public void ProviderConfigUpdateRequest_ReplacesExistingKeyWhenNewKeyIsProvided()
    {
        var clientConfig = new ProviderConfig
        {
            ProviderId = "openai",
            ApiKey = "replacement-secret",
            HasStoredApiKey = true,
        };
        var existing = new ProviderConfig
        {
            ProviderId = "openai",
            ApiKey = "existing-secret",
        };

        var request = ProviderConfigUpdateRequest.FromProviderConfig(clientConfig);
        var merged = request.ToProviderConfig(existing);

        Assert.False(request.PreserveApiKey);
        Assert.Equal("replacement-secret", merged.ApiKey);
    }

    [Fact]
    public async Task AuthenticationMiddleware_AllowsHubWithBearerTokenAsync()
    {
        var nextCalled = false;
        var middleware = new MonitorApiAuthenticationMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            AccessToken);
        var context = new DefaultHttpContext();
        context.Request.Path = MonitorApiRoutes.HubUsage;
        context.Request.Headers.Authorization = $"Bearer {AccessToken}";

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Equal((int)HttpStatusCode.OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task AuthenticationMiddleware_AllowsHubWithAccessTokenQueryAsync()
    {
        var nextCalled = false;
        var middleware = new MonitorApiAuthenticationMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            AccessToken);
        var context = new DefaultHttpContext();
        context.Request.Path = MonitorApiRoutes.HubUsage;
        context.Request.QueryString = new QueryString("?access_token=" + Uri.EscapeDataString(AccessToken));

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Equal((int)HttpStatusCode.OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task AuthenticationMiddleware_RejectsHubWithoutTokenAsync()
    {
        var nextCalled = false;
        var middleware = new MonitorApiAuthenticationMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            AccessToken);
        var context = new DefaultHttpContext();
        context.Request.Path = MonitorApiRoutes.HubUsage;

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal((int)HttpStatusCode.Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task AuthenticationMiddleware_RejectsHubWithWrongTokenAsync()
    {
        var nextCalled = false;
        var middleware = new MonitorApiAuthenticationMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            AccessToken);
        var context = new DefaultHttpContext();
        context.Request.Path = MonitorApiRoutes.HubUsage;
        context.Request.Headers.Authorization = "Bearer wrong-token-value";

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal((int)HttpStatusCode.Unauthorized, context.Response.StatusCode);
    }
}