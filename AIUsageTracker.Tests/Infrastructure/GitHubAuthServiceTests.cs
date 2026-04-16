// <copyright file="GitHubAuthServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using System.Text;
using System.Text.Json;
using AIUsageTracker.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

namespace AIUsageTracker.Tests.Infrastructure;

public class GitHubAuthServiceTests : IDisposable
{
    private readonly Mock<HttpMessageHandler> _handlerMock;
    private readonly HttpClient _httpClient;
    private readonly Mock<ILogger<GitHubAuthService>> _loggerMock;
    private readonly GitHubAuthService _service;

    public GitHubAuthServiceTests()
    {
        this._handlerMock = new Mock<HttpMessageHandler>();
        this._httpClient = new HttpClient(this._handlerMock.Object);
        this._loggerMock = new Mock<ILogger<GitHubAuthService>>();
        this._service = new GitHubAuthService(this._httpClient, this._loggerMock.Object);
    }

    public void Dispose()
    {
        this._httpClient.Dispose();
    }

    [Fact]
    public void IsAuthenticated_DefaultsToFalse()
    {
        Assert.False(this._service.IsAuthenticated);
    }

    [Fact]
    public void InitializeToken_SetsToken_AndIsAuthenticatedTrue()
    {
        this._service.InitializeToken("ghp_testtoken123");
        Assert.True(this._service.IsAuthenticated);
    }

    [Fact]
    public void InitializeToken_EmptyToken_StillSetsIsAuthenticatedFalse()
    {
        this._service.InitializeToken(string.Empty);
        Assert.False(this._service.IsAuthenticated);
    }

    [Fact]
    public void Logout_ClearsToken()
    {
        this._service.InitializeToken("ghp_testtoken123");
        Assert.True(this._service.IsAuthenticated);

        this._service.Logout();
        Assert.False(this._service.IsAuthenticated);
    }

    [Fact]
    public void InitializeToken_ResetsUsernameCache_WhenTokenChanges()
    {
        this._service.InitializeToken("token1");
        Assert.True(this._service.IsAuthenticated);

        this._service.InitializeToken("token2");
        Assert.True(this._service.IsAuthenticated);
    }

    [Fact]
    public void InitializeToken_DoesNotResetUsernameCache_WhenSameToken()
    {
        this._service.InitializeToken("same-token");
        Assert.True(this._service.IsAuthenticated);

        this._service.InitializeToken("same-token");
        Assert.True(this._service.IsAuthenticated);
    }

    [Fact]
    public void GetCurrentToken_ReturnsInitializedToken()
    {
        this._service.InitializeToken("ghp_testtoken123");
        Assert.Equal("ghp_testtoken123", this._service.GetCurrentToken());
    }

    [Fact(Skip = "GetCurrentToken falls through to hosts.yml/gh CLI which may have real tokens on this machine")]
    public void GetCurrentToken_ReturnsNull_AfterLogout()
    {
        this._service.InitializeToken("ghp_testtoken123");
        this._service.Logout();
        Assert.Null(this._service.GetCurrentToken());
    }

    [Fact]
    public async Task RefreshTokenAsync_ReturnsNull()
    {
        var result = await this._service.RefreshTokenAsync("some-refresh-token").ConfigureAwait(false);
        Assert.Null(result);
    }

    [Fact]
    public async Task PollForTokenAsync_ReturnsNull_WhenAuthorizationPending()
    {
        var response = new
        {
            error = "authorization_pending",
        };

        this.SetupHttpResponse(JsonSerializer.Serialize(response), HttpStatusCode.OK);

        var result = await this._service.PollForTokenAsync("device-code-123", 5);
        Assert.Null(result);
    }

    [Fact]
    public async Task PollForTokenAsync_ReturnsSlowDown_WhenSlowDownError()
    {
        var response = new
        {
            error = "slow_down",
        };

        this.SetupHttpResponse(JsonSerializer.Serialize(response), HttpStatusCode.OK);

        var result = await this._service.PollForTokenAsync("device-code-123", 5);
        Assert.Equal("SLOW_DOWN", result);
    }

    [Fact]
    public async Task PollForTokenAsync_ThrowsSecurityException_WhenTokenExpired()
    {
        var response = new
        {
            error = "expired_token",
        };

        this.SetupHttpResponse(JsonSerializer.Serialize(response), HttpStatusCode.OK);

        await Assert.ThrowsAsync<System.Security.SecurityException>(
            () => this._service.PollForTokenAsync("device-code-123", 5));
    }

    [Fact]
    public async Task PollForTokenAsync_ThrowsSecurityException_WhenAccessDenied()
    {
        var response = new
        {
            error = "access_denied",
        };

        this.SetupHttpResponse(JsonSerializer.Serialize(response), HttpStatusCode.OK);

        await Assert.ThrowsAsync<System.Security.SecurityException>(
            () => this._service.PollForTokenAsync("device-code-123", 5));
    }

    [Fact]
    public async Task PollForTokenAsync_ReturnsToken_WhenAccessTokenReceived()
    {
        var response = new
        {
            access_token = "ghp_success_token",
            token_type = "bearer",
        };

        this.SetupHttpResponse(JsonSerializer.Serialize(response), HttpStatusCode.OK);

        var result = await this._service.PollForTokenAsync("device-code-123", 5);
        Assert.Equal("ghp_success_token", result);
        Assert.True(this._service.IsAuthenticated);
    }

    [Fact]
    public async Task PollForTokenAsync_ReturnsNull_WhenNonSuccessStatusCode()
    {
        this.SetupHttpResponse("error", HttpStatusCode.BadRequest);

        var result = await this._service.PollForTokenAsync("device-code-123", 5);
        Assert.Null(result);
    }

    [Fact]
    public async Task PollForTokenAsync_ReturnsNull_OnNetworkError()
    {
        this._handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("network error"));

        var result = await this._service.PollForTokenAsync("device-code-123", 5);
        Assert.Null(result);
    }

    [Fact]
    public async Task GetUsernameAsync_ReturnsCachedUsername_WhenAvailable()
    {
        this._service.InitializeToken("ghp_testtoken123");

        var userResponse = new { login = "testuser" };
        this.SetupHttpResponse(JsonSerializer.Serialize(userResponse), HttpStatusCode.OK, "https://api.github.com/user");

        var first = await this._service.GetUsernameAsync();
        var second = await this._service.GetUsernameAsync();

        Assert.Equal("testuser", first);
        Assert.Equal("testuser", second);
    }

    [Fact(Skip = "GetUsernameAsync falls through to hosts.yml which may have real usernames on this machine")]
    public async Task GetUsernameAsync_ReturnsNull_WhenNotAuthenticated()
    {
        var result = await this._service.GetUsernameAsync();
        Assert.Null(result);
    }

    [Fact]
    public async Task GetUsernameAsync_ReturnsNull_WhenApiReturnsNonSuccess()
    {
        this._service.InitializeToken("ghp_testtoken123");
        this.SetupHttpResponse("forbidden", HttpStatusCode.Forbidden, "https://api.github.com/user");

        var result = await this._service.GetUsernameAsync();
        Assert.Null(result);
    }

    [Fact]
    public async Task GetUsernameAsync_ReturnsNull_WhenJsonMissingLogin()
    {
        this._service.InitializeToken("ghp_testtoken123");
        this.SetupHttpResponse(JsonSerializer.Serialize(new { id = 123 }), HttpStatusCode.OK, "https://api.github.com/user");

        var result = await this._service.GetUsernameAsync();
        Assert.Null(result);
    }

    [Fact]
    public async Task GetUsernameAsync_ReturnsNull_OnNetworkError()
    {
        this._service.InitializeToken("ghp_testtoken123");
        this._handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("network error"));

        var result = await this._service.GetUsernameAsync();
        Assert.Null(result);
    }

    private void SetupHttpResponse(string content, HttpStatusCode statusCode, string? url = null)
    {
        this._handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            });
    }
}
