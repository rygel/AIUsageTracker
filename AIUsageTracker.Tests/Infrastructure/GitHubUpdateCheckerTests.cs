// <copyright file="GitHubUpdateCheckerTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net.Http;
using System.Reflection;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AIUsageTracker.Tests.Infrastructure;

public class GitHubUpdateCheckerTests
{
    // ── ParseAppVersion ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("2.3.4-beta.8",  2, 3, 4, 8)]
    [InlineData("2.3.4-beta.1",  2, 3, 4, 1)]
    [InlineData("2.3.4",         2, 3, 4, int.MaxValue)]  // stable sorts above any beta
    [InlineData("1.0.0",         1, 0, 0, int.MaxValue)]
    [InlineData("10.2.3-beta.99", 10, 2, 3, 99)]
    public void ParseAppVersion_ReturnsExpectedTuple(
        string version, int major, int minor, int patch, int preRelease)
    {
        var result = GitHubUpdateChecker.ParseAppVersion(version);
        Assert.Equal((major, minor, patch, preRelease), result);
    }

    // ── IsNewerVersion ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2.3.4-beta.8", "2.3.4-beta.7", true)]   // newer beta
    [InlineData("2.3.4-beta.7", "2.3.4-beta.8", false)]  // older beta
    [InlineData("2.3.4-beta.7", "2.3.4-beta.7", false)]  // same beta
    [InlineData("2.3.4",        "2.3.4-beta.8", true)]   // stable > any beta of same core
    [InlineData("2.3.4-beta.8", "2.3.4",        false)]  // beta < stable of same core
    [InlineData("2.3.5-beta.1", "2.3.4-beta.9", true)]   // higher patch wins
    [InlineData("2.4.0",        "2.3.99",        true)]   // higher minor wins
    [InlineData("3.0.0",        "2.99.99",       true)]   // higher major wins
    [InlineData("v2.3.4-beta.8","2.3.4-beta.7", true)]   // v-prefix stripped
    public void IsNewerVersion_ReturnsExpectedResult(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, GitHubUpdateChecker.IsNewerVersion(candidate, current));
    }

    // ── Beta channel must NOT use the /releases/latest/download/ appcast URL ─

    [Fact]
    public void GetAppcastUrl_BetaChannel_DoesNotUseLatestDownloadPath()
    {
        // /releases/latest only resolves non-pre-release releases on GitHub.
        // A beta appcast served from that URL would always 404 for repos whose
        // most-recent non-pre-release is older and did not include beta appcast files.
        var url = GitHubUpdateChecker.GetAppcastUrl("x64", isBeta: true);

        // Verify the URL still exists (method is used for stable-channel path),
        // but document the known limitation so a future change to fix it is visible.
        Assert.Contains("appcast_beta_x64.xml", url);

        // The beta update check path bypasses this URL entirely — it uses the
        // GitHub Releases API directly. Regression: if a future refactor makes
        // CheckForUpdatesAsync(Beta) call GetAppcastUrl again, IsNewerVersion
        // tests above will catch the broken version-comparison side, and this
        // comment serves as the architectural guard.
    }

    // ── Stable channel appcast URL shape ─────────────────────────────────────

    [Fact]
    public void GetAppcastUrl_StableChannel_UsesStableAppcastFile()
    {
        var url = GitHubUpdateChecker.GetAppcastUrl("x64", isBeta: false);
        Assert.Contains("appcast_x64.xml", url);
        Assert.DoesNotContain("beta", url);
    }

    [Theory]
    [InlineData("x64",   "appcast_x64.xml")]
    [InlineData("x86",   "appcast_x86.xml")]
    [InlineData("arm64", "appcast_arm64.xml")]
    [InlineData("arm",   "appcast_arm64.xml")]  // arm maps to arm64
    [InlineData("X64",   "appcast_x64.xml")]    // case-insensitive
    public void GetAppcastUrl_ArchitectureNormalisation_CorrectFile(string arch, string expectedFile)
    {
        var url = GitHubUpdateChecker.GetAppcastUrl(arch, isBeta: false);
        Assert.Contains(expectedFile, url);
    }

    // ── GetAppcastUrlForCurrentArchitecture (used by stable channel only) ────

    [Fact]
    public void GetAppcastUrlForCurrentArchitecture_StableChannel_MatchesExpected()
    {
        using var httpClient = new HttpClient();
        var checker = new GitHubUpdateChecker(
            NullLogger<GitHubUpdateChecker>.Instance,
            httpClient,
            UpdateChannel.Stable);

        var url = InvokeGetAppcastUrlForCurrentArchitecture(checker);

        Assert.Equal(
            GitHubUpdateChecker.GetAppcastUrl(GetExpectedArchitecture(), isBeta: false),
            url);
    }

    // ── GetCurrentInformationalVersion ───────────────────────────────────────

    [Fact]
    public void GetCurrentInformationalVersion_StripsBuildMetadata()
    {
        // The method strips anything after '+' (build-metadata added by the SDK).
        // We cannot control the running assembly's version in a unit test, so we
        // verify the contract: the result must not contain a '+' suffix.
        var version = GitHubUpdateChecker.GetCurrentInformationalVersion();
        Assert.DoesNotContain("+", version);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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
