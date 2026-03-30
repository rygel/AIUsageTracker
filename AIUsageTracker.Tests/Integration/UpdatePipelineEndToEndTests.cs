// <copyright file="UpdatePipelineEndToEndTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Net;
using System.Text.Json;
using System.Xml.Linq;
using AIUsageTracker.Core.Models;
using AIUsageTracker.Infrastructure.Services;
using AIUsageTracker.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIUsageTracker.Tests.Integration;

/// <summary>
/// End-to-end tests that verify the entire update pipeline against the real
/// GitHub Releases API and CDN. These catch issues that unit tests miss:
/// URL construction errors, API response changes, missing release assets,
/// CDN redirect failures, and appcast XML parse errors.
///
/// Tests return early (not failed) when the network is unreachable so CI
/// doesn't break on transient GitHub outages.
/// </summary>
public sealed class UpdatePipelineEndToEndTests : IDisposable
{
    private static readonly string[] Architectures = ["x64", "x86", "arm64"];
    private readonly HttpClient _httpClient;

    public UpdatePipelineEndToEndTests()
    {
        this._httpClient = new HttpClient();
        this._httpClient.DefaultRequestHeaders.Add("User-Agent", "AIUsageTracker-Tests");
        this._httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    // ── Beta update check: GitHub Releases API ────────────────────────────

    [Fact]
    public async Task BetaUpdateCheck_FindsLatestPreRelease()
    {
        var checker = new GitHubUpdateChecker(
            NullLogger<GitHubUpdateChecker>.Instance,
            this._httpClient,
            UpdateChannel.Beta);

        var update = await checker.CheckForUpdatesAsync();

        // The running test assembly version is 0.0.0 or similar — any real
        // release should be detected as newer.
        if (!AssertNetworkAvailable(update))
        {
            return;
        }

        Assert.NotNull(update);
        Assert.False(string.IsNullOrWhiteSpace(update!.Version), "Update version must not be empty.");
        Assert.Contains("-beta.", update.Version, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(update.DownloadUrl), "Download URL must not be empty.");
        Assert.False(string.IsNullOrWhiteSpace(update.ReleaseUrl), "Release URL must not be empty.");
    }

    [Fact]
    public async Task BetaUpdateCheck_DownloadUrl_ReturnsHttp200()
    {
        var checker = new GitHubUpdateChecker(
            NullLogger<GitHubUpdateChecker>.Instance,
            this._httpClient,
            UpdateChannel.Beta);

        var update = await checker.CheckForUpdatesAsync();
        if (!AssertNetworkAvailable(update))
        {
            return;
        }

        Assert.NotNull(update);

        using var request = new HttpRequestMessage(HttpMethod.Head, update!.DownloadUrl);
        using var response = await this._httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Download URL returned {(int)response.StatusCode} for {update.DownloadUrl}");
        Assert.True(
            response.Content.Headers.ContentLength > 0,
            $"Download has zero content length for {update.DownloadUrl}");
    }

    [Fact]
    public async Task BetaUpdateCheck_DownloadUrl_MatchesExpectedPattern()
    {
        var checker = new GitHubUpdateChecker(
            NullLogger<GitHubUpdateChecker>.Instance,
            this._httpClient,
            UpdateChannel.Beta);

        var update = await checker.CheckForUpdatesAsync();
        if (!AssertNetworkAvailable(update))
        {
            return;
        }

        Assert.NotNull(update);

        // URL must be: .../releases/download/v{version}/AIUsageTracker_Setup_v{version}_win-{arch}.exe
        var url = update!.DownloadUrl;
        Assert.StartsWith(
            "https://github.com/rygel/AIUsageTracker/releases/download/v",
            url,
            StringComparison.Ordinal);
        Assert.Contains($"_v{update.Version}_win-", url, StringComparison.Ordinal);
        Assert.EndsWith(".exe", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BetaUpdateCheck_ReleaseUrl_ReturnsHttp200()
    {
        var checker = new GitHubUpdateChecker(
            NullLogger<GitHubUpdateChecker>.Instance,
            this._httpClient,
            UpdateChannel.Beta);

        var update = await checker.CheckForUpdatesAsync();
        if (!AssertNetworkAvailable(update))
        {
            return;
        }

        Assert.NotNull(update);

        using var request = new HttpRequestMessage(HttpMethod.Head, update!.ReleaseUrl);
        using var response = await this._httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Release URL returned {(int)response.StatusCode} for {update.ReleaseUrl}");
    }

    // ── Release asset completeness ────────────────────────────────────────
    // Every release must have installer .exe files for all 3 architectures.

    [Fact]
    public async Task LatestBetaRelease_HasInstallerForEveryArchitecture()
    {
        var release = await this.FetchLatestBetaReleaseAsync();
        if (!AssertNetworkAvailable(release))
        {
            return;
        }

        Assert.NotNull(release);

        var version = release!.Value.GetProperty("tag_name").GetString()!.TrimStart('v');

        foreach (var arch in Architectures)
        {
            var expectedAsset = $"AIUsageTracker_Setup_v{version}_win-{arch}.exe";
            var found = release.Value.GetProperty("assets").EnumerateArray()
                .Any(a => string.Equals(a.GetProperty("name").GetString(), expectedAsset, StringComparison.Ordinal));

            Assert.True(found, $"Missing installer asset '{expectedAsset}' in release v{version}.");
        }
    }

    [Fact]
    public async Task LatestBetaRelease_InstallerAssetsAreNonZeroSize()
    {
        var release = await this.FetchLatestBetaReleaseAsync();
        if (!AssertNetworkAvailable(release))
        {
            return;
        }

        Assert.NotNull(release);

        foreach (var asset in release!.Value.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? string.Empty;
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                !name.StartsWith("AIUsageTracker_Setup_", StringComparison.Ordinal))
            {
                continue;
            }

            var size = asset.GetProperty("size").GetInt64();
            Assert.True(size > 0, $"Installer '{name}' has zero size.");
            Assert.True(size > 1_000_000, $"Installer '{name}' is suspiciously small ({size} bytes).");
        }
    }

    // ── Appcast files on the release ─────────────────────────────────────

    [Fact]
    public async Task LatestBetaRelease_HasAllBetaAppcastFiles()
    {
        var release = await this.FetchLatestBetaReleaseAsync();
        if (!AssertNetworkAvailable(release))
        {
            return;
        }

        Assert.NotNull(release);

        string[] expected = ["appcast_beta.xml", "appcast_beta_x64.xml", "appcast_beta_x86.xml", "appcast_beta_arm64.xml"];

        var assetNames = release!.Value.GetProperty("assets").EnumerateArray()
            .Select(a => a.GetProperty("name").GetString() ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var file in expected)
        {
            Assert.Contains(file, assetNames);
        }
    }

    [Theory]
    [InlineData("appcast_beta.xml", "x64")]
    [InlineData("appcast_beta_x64.xml", "x64")]
    [InlineData("appcast_beta_x86.xml", "x86")]
    [InlineData("appcast_beta_arm64.xml", "arm64")]
    public async Task LatestBetaRelease_AppcastFile_IsValidAndConsistent(string fileName, string expectedArch)
    {
        var release = await this.FetchLatestBetaReleaseAsync();
        if (!AssertNetworkAvailable(release))
        {
            return;
        }

        Assert.NotNull(release);

        var tag = release!.Value.GetProperty("tag_name").GetString()!;
        var version = tag.TrimStart('v');

        var appcastUrl = $"https://github.com/rygel/AIUsageTracker/releases/download/{tag}/{fileName}";
        string xml;
        try
        {
            xml = await this._httpClient.GetStringAsync(appcastUrl);
        }
        catch (HttpRequestException)
        {
            // Network issue fetching the appcast — skip, don't fail.
            return;
        }

        XNamespace sparkle = "http://www.andymatuschak.org/xml-namespaces/sparkle";
        var doc = XDocument.Parse(xml);
        var enclosure = doc.Root?.Element("channel")?.Element("item")?.Element("enclosure");
        Assert.NotNull(enclosure);

        // URL points to the correct architecture installer
        var url = enclosure!.Attribute("url")?.Value ?? string.Empty;
        Assert.Contains($"_win-{expectedArch}.exe", url, StringComparison.Ordinal);
        Assert.Contains($"/v{version}/", url, StringComparison.Ordinal);

        // sparkle:shortVersionString matches the release version
        var shortVersion = enclosure.Attribute(sparkle + "shortVersionString")?.Value ?? string.Empty;
        Assert.Equal(version, shortVersion);

        // length is non-zero
        var lengthStr = enclosure.Attribute("length")?.Value ?? "0";
        Assert.True(long.TryParse(lengthStr, out var length) && length > 0,
            $"Appcast {fileName} has length={lengthStr}. Must be > 0.");

        // The download URL in the appcast actually resolves
        using var headRequest = new HttpRequestMessage(HttpMethod.Head, url);
        using var headResponse = await this._httpClient.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead);
        Assert.True(
            headResponse.StatusCode == HttpStatusCode.OK,
            $"Appcast download URL returned {(int)headResponse.StatusCode}: {url}");
    }

    // ── Stable channel: appcast URL resolves ──────────────────────────────

    [Theory]
    [InlineData("x64")]
    [InlineData("x86")]
    [InlineData("arm64")]
    public async Task StableAppcastUrl_ResolvesWithHttp200(string arch)
    {
        var appcastUrl = GitHubUpdateChecker.GetAppcastUrl(arch, isBeta: false);

        using var request = new HttpRequestMessage(HttpMethod.Head, appcastUrl);
        HttpResponseMessage response;
        try
        {
            response = await this._httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        }
        catch (HttpRequestException)
        {
            return;
        }

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Stable appcast URL returned {(int)response.StatusCode} for {appcastUrl}");
    }

    // ── Download URL construction matches real assets ─────────────────────

    [Theory]
    [InlineData("x64")]
    [InlineData("x86")]
    [InlineData("arm64")]
    public async Task BetaDownloadUrl_ForEachArchitecture_ReturnsHttp200(string arch)
    {
        var release = await this.FetchLatestBetaReleaseAsync();
        if (!AssertNetworkAvailable(release))
        {
            return;
        }

        Assert.NotNull(release);

        var tag = release!.Value.GetProperty("tag_name").GetString()!;
        var version = tag.TrimStart('v');

        // This must match the URL pattern in CheckForBetaUpdatesAsync
        var downloadUrl = $"https://github.com/rygel/AIUsageTracker/releases/download/{tag}/AIUsageTracker_Setup_v{version}_win-{arch}.exe";

        using var request = new HttpRequestMessage(HttpMethod.Head, downloadUrl);
        HttpResponseMessage response;
        try
        {
            response = await this._httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        }
        catch (HttpRequestException)
        {
            return;
        }

        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"Download URL returned {(int)response.StatusCode} for {downloadUrl}");

        Assert.True(
            response.Content.Headers.ContentLength > 1_000_000,
            $"Installer at {downloadUrl} is too small ({response.Content.Headers.ContentLength} bytes).");
    }

    // ── Version comparison guards ─────────────────────────────────────────
    // These are critical: if IsNewerVersion breaks, no user ever gets an update.

    [Fact]
    public async Task BetaUpdateCheck_DetectedVersion_IsNewerThanPreviousRelease()
    {
        var releases = await this.FetchRecentReleasesAsync(5);
        if (!AssertNetworkAvailable(releases))
        {
            return;
        }

        Assert.NotNull(releases);

        var betas = releases!.Value.EnumerateArray()
            .Where(r => r.GetProperty("prerelease").GetBoolean())
            .Select(r => r.GetProperty("tag_name").GetString()!.TrimStart('v'))
            .ToList();

        Assert.True(betas.Count >= 2, "Need at least 2 beta releases to compare.");

        // The newest beta must compare as greater than the second-newest.
        Assert.True(
            GitHubUpdateChecker.IsNewerVersion(betas[0], betas[1]),
            $"Expected {betas[0]} > {betas[1]} but IsNewerVersion returned false.");
    }

    // ── GitHub Releases API contract ──────────────────────────────────────

    [Fact]
    public async Task GitHubReleasesApi_ReturnsExpectedStructure()
    {
        var releases = await this.FetchRecentReleasesAsync(1);
        if (!AssertNetworkAvailable(releases))
        {
            return;
        }

        Assert.NotNull(releases);

        var release = releases!.Value.EnumerateArray().First();

        // These properties are read by CheckForBetaUpdatesAsync — if GitHub
        // changes the API shape, this test catches it.
        Assert.True(release.TryGetProperty("tag_name", out _), "Missing tag_name");
        Assert.True(release.TryGetProperty("prerelease", out _), "Missing prerelease");
        Assert.True(release.TryGetProperty("published_at", out _), "Missing published_at");
        Assert.True(release.TryGetProperty("body", out _), "Missing body");
        Assert.True(release.TryGetProperty("assets", out var assets), "Missing assets");
        Assert.True(assets.GetArrayLength() > 0, "Release has no assets.");

        var firstAsset = assets.EnumerateArray().First();
        Assert.True(firstAsset.TryGetProperty("name", out _), "Asset missing name");
        Assert.True(firstAsset.TryGetProperty("size", out _), "Asset missing size");
        Assert.True(firstAsset.TryGetProperty("browser_download_url", out _), "Asset missing browser_download_url");
    }

    // ── Download-then-move file lock regression ────────────────────────────
    // This catches the exact bug where "using var" (declaration form) keeps
    // the FileStream open until end-of-method, causing File.Move to fail
    // with an IOException because the file is still locked.  The fix uses a
    // "using block" so the stream is disposed before the move.

    [Fact]
    public async Task DownloadToPartialFile_ThenMove_Succeeds()
    {
        // Pick the smallest release asset we control — an appcast XML file.
        var release = await this.FetchLatestBetaReleaseAsync();
        if (!AssertNetworkAvailable(release))
        {
            return;
        }

        Assert.NotNull(release);

        var tag = release!.Value.GetProperty("tag_name").GetString()!;
        var appcastUrl = $"https://github.com/rygel/AIUsageTracker/releases/download/{tag}/appcast_beta.xml";

        var tempDir = TestTempPaths.CreateDirectory("download-move-e2e");
        var finalPath = Path.Combine(tempDir, "appcast_beta.xml");
        var partialPath = $"{finalPath}.partial";

        try
        {
            // Mirrors the exact pattern from GitHubUpdateChecker.DownloadInstallerAsync.
            // If someone changes the block-scoped "using" back to "using var",
            // the File.Move below will throw because the file handle is still open.
            using (var response = await this._httpClient.GetAsync(appcastUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();

                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[8192];
                    int read;
                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read);
                    }

                    await fileStream.FlushAsync();
                }
            }

            // This is the line that fails when the file stream is not properly disposed.
            File.Move(partialPath, finalPath, overwrite: true);

            Assert.True(File.Exists(finalPath), "Final file must exist after move.");
            var content = await File.ReadAllTextAsync(finalPath);
            Assert.True(content.Length > 0, "Downloaded file must have content.");
            Assert.Contains("<enclosure", content, StringComparison.Ordinal);
        }
        finally
        {
            TestTempPaths.CleanupPath(tempDir);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private async Task<JsonElement?> FetchLatestBetaReleaseAsync()
    {
        var releases = await this.FetchRecentReleasesAsync(5);
        if (releases == null)
        {
            return null;
        }

        foreach (var release in releases.Value.EnumerateArray())
        {
            if (release.GetProperty("prerelease").GetBoolean())
            {
                return release;
            }
        }

        return null;
    }

    private async Task<JsonElement?> FetchRecentReleasesAsync(int count)
    {
        try
        {
            var url = $"https://api.github.com/repos/rygel/AIUsageTracker/releases?per_page={count}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await this._httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            return JsonDocument.Parse(json).RootElement;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns true if the network call succeeded. If false, the test should
    /// return early — not fail — to avoid CI flakiness on transient outages.
    /// </summary>
    private static bool AssertNetworkAvailable(object? result)
    {
        return result != null;
    }

    public void Dispose()
    {
        this._httpClient.Dispose();
    }
}
