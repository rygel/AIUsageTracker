// <copyright file="GitHubUpdateCheckerExtendedTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using System.Text;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;

namespace AIUsageTracker.Tests.Infrastructure;

public class GitHubUpdateCheckerExtendedTests : IDisposable
{
    private readonly Mock<HttpMessageHandler> _handlerMock;
    private readonly HttpClient _httpClient;
    private readonly GitHubUpdateChecker _checker;

    public GitHubUpdateCheckerExtendedTests()
    {
        this._handlerMock = new Mock<HttpMessageHandler>();
        this._httpClient = new HttpClient(this._handlerMock.Object);
        this._checker = new GitHubUpdateChecker(
            NullLogger<GitHubUpdateChecker>.Instance,
            this._httpClient,
            UpdateChannel.Beta);
    }

    public void Dispose()
    {
        this._httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData("0.0.1", 0, 0, 1, int.MaxValue)]
    [InlineData("99.99.99-beta.99", 99, 99, 99, 99)]
    [InlineData("1.0.0-beta.0", 1, 0, 0, 0)]
    public void ParseAppVersion_EdgeCases(string version, int major, int minor, int patch, int preRelease)
    {
        var result = GitHubUpdateChecker.ParseAppVersion(version);
        Assert.Equal((major, minor, patch, preRelease), result);
    }

    [Theory]
    [InlineData("", 0, 0, 0, int.MaxValue)]
    [InlineData("not-a-version", 0, 0, 0, int.MaxValue)]
    public void ParseAppVersion_InvalidVersions_DefaultToZero(string version, int major, int minor, int patch, int preRelease)
    {
        var result = GitHubUpdateChecker.ParseAppVersion(version);
        Assert.Equal((major, minor, patch, preRelease), result);
    }

    [Theory]
    [InlineData("2.0.0", "1.0.0", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0.0", "2.0.0", false)]
    public void IsNewerVersion_StableVersions(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, GitHubUpdateChecker.IsNewerVersion(candidate, current));
    }

    [Fact]
    public async Task CheckForUpdatesAsync_BetaChannel_ReturnsNull_WhenApiReturnsNonSuccess()
    {
        this.SetupHttpResponse("not found", HttpStatusCode.NotFound);

        var result = await this._checker.CheckForUpdatesAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_BetaChannel_ReturnsNull_WhenNoPrereleases()
    {
        var releases = new[]
        {
            new { prerelease = false, tag_name = "v1.0.0", published_at = "2026-01-01T00:00:00Z", body = "stable" },
        };

        this.SetupHttpResponse(JsonSerializer.Serialize(releases), HttpStatusCode.OK);

        var result = await this._checker.CheckForUpdatesAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_BetaChannel_ReturnsNull_WhenPrereleaseIsOlder()
    {
        var currentVersion = GitHubUpdateChecker.GetCurrentInformationalVersion();

        var releases = new[]
        {
            new { prerelease = true, tag_name = "v0.0.1-beta.1", published_at = "2020-01-01T00:00:00Z", body = "old beta" },
        };

        this.SetupHttpResponse(JsonSerializer.Serialize(releases), HttpStatusCode.OK);

        var result = await this._checker.CheckForUpdatesAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task DownloadAndInstallUpdateAsync_ThrowsOnNullUpdateInfo()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => this._checker.DownloadAndInstallUpdateAsync(null!));
    }

    [Fact]
    public async Task DownloadAndInstallUpdateAsync_ReturnsFail_WhenNoDownloadUrl()
    {
        var updateInfo = new AIUsageTracker.Core.Interfaces.UpdateInfo
        {
            Version = "1.0.0",
            DownloadUrl = string.Empty,
        };

        var result = await this._checker.DownloadAndInstallUpdateAsync(updateInfo);
        Assert.False(result.Success);
        Assert.Contains("No download URL", result.FailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadAndInstallUpdateAsync_ReturnsFail_WhenDownloadFails()
    {
        this._handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("download failed"));

        var updateInfo = new AIUsageTracker.Core.Interfaces.UpdateInfo
        {
            Version = "1.0.0",
            DownloadUrl = "https://example.com/setup.exe",
        };

        var result = await this._checker.DownloadAndInstallUpdateAsync(updateInfo);
        Assert.False(result.Success);
    }

    [Fact]
    public void UpdateInstallResult_Ok_ReturnsSuccess()
    {
        var result = GitHubUpdateChecker.UpdateInstallResult.Ok();
        Assert.True(result.Success);
        Assert.Equal(string.Empty, result.FailureReason);
    }

    [Fact]
    public void UpdateInstallResult_Fail_ReturnsFailure()
    {
        var result = GitHubUpdateChecker.UpdateInstallResult.Fail("test reason");
        Assert.False(result.Success);
        Assert.Equal("test reason", result.FailureReason);
    }

    private void SetupHttpResponse(string content, HttpStatusCode statusCode)
    {
        this._handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            });
    }
}
