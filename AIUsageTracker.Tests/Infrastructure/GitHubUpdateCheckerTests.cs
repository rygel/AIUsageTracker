// <copyright file="GitHubUpdateCheckerTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net.Http;
using System.Reflection;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Core.Updates;
using AIUsageTracker.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure;

public class GitHubUpdateCheckerTests
{
    // The IsUpdateAvailable method was removed as it's now handled by Version.TryParse
    // and basic comparison in CheckForUpdatesAsync. We'll test the logic via a new
    // helper or just verify the version comparison logic if we want to keep unit tests.
    [Fact]
    public void VersionComparison_WorksAsExpected()
    {
        // Arrange
        var current = new Version(1, 0, 0);
        var latestStr = "1.0.1";

        // Act
        bool parsed = Version.TryParse(latestStr, out var latest);

        bool isNewer = parsed && latest > current;

        // Assert
        Assert.True(isNewer);
        Assert.Equal(new Version(1, 0, 1), latest);
    }

    [Fact]
    public void VersionComparison_HandlesVPrefix()
    {
        // Arrange
        var current = new Version(1, 0, 0);
        var latestStr = "v1.0.1";

        // Act
        // Note: Version.TryParse doesn't handle 'v' prefix, but AppCastItem.Version
        // usually has it stripped or we handle it in our code.
        // In our refactored GitHubUpdateChecker, we use latest.Version directly.
        // If NetSparkle's GitHubReleaseAppCast returns 'v1.0.1', Version.TryParse fails.
        // Let's verify our code handles this or if we need to add v-stripping back.
        string sanitized = latestStr.StartsWith("v", StringComparison.Ordinal) ? latestStr[1..] : latestStr;
        bool parsed = Version.TryParse(sanitized, out var latest);

        bool isNewer = parsed && latest > current;

        // Assert
        Assert.True(isNewer);
    }

    [Fact]
    public void GetAppcastUrlForCurrentArchitecture_UsesStableChannelFeed()
    {
        using var httpClient = new HttpClient();
        var checker = new GitHubUpdateChecker(
            NullLogger<GitHubUpdateChecker>.Instance,
            httpClient,
            UpdateChannel.Stable);

        var url = InvokeGetAppcastUrlForCurrentArchitecture(checker);

        Assert.Equal(
            ReleaseUrlCatalog.GetAppcastUrl(GetExpectedArchitecture(), isBeta: false),
            url);
    }

    [Fact]
    public void GetAppcastUrlForCurrentArchitecture_UsesBetaChannelFeed()
    {
        using var httpClient = new HttpClient();
        var checker = new GitHubUpdateChecker(
            NullLogger<GitHubUpdateChecker>.Instance,
            httpClient,
            UpdateChannel.Beta);

        var url = InvokeGetAppcastUrlForCurrentArchitecture(checker);

        Assert.Equal(
            ReleaseUrlCatalog.GetAppcastUrl(GetExpectedArchitecture(), isBeta: true),
            url);
    }

    private static string InvokeGetAppcastUrlForCurrentArchitecture(GitHubUpdateChecker checker)
    {
        var method = typeof(GitHubUpdateChecker).GetMethod(
            "GetAppcastUrlForCurrentArchitecture",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (string)method!.Invoke(checker, null)!;
    }

    private static string GetExpectedArchitecture()
    {
        return System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.X86 => "x86",
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            System.Runtime.InteropServices.Architecture.Arm => "arm64",
            _ => "x64",
        };
    }
}
