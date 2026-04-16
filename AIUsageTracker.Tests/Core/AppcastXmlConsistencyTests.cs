// <copyright file="AppcastXmlConsistencyTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Xml.Linq;

namespace AIUsageTracker.Tests.Core;

/// <summary>
/// Guards against regressions in the committed appcast XML files.
///
/// These files are the on-disk source that CI uploads to each GitHub Release as attached
/// assets. Clients (NetSparkle / Sparkle) download them to discover available updates, so
/// any structural error here directly breaks auto-update for all users on that channel.
///
/// Failures here indicate something went wrong during the release process (e.g. appcast
/// committed without real installer sizes, version mismatch across arch files, etc.).
/// </summary>
public sealed class AppcastXmlConsistencyTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly XNamespace Sparkle = "http://www.andymatuschak.org/xml-namespaces/sparkle";

    // ── File existence ────────────────────────────────────────────────────────
    [Theory]
    [InlineData("appcast.xml")]
    [InlineData("appcast_x64.xml")]
    [InlineData("appcast_x86.xml")]
    [InlineData("appcast_arm64.xml")]
    [InlineData("appcast_beta.xml")]
    [InlineData("appcast_beta_x64.xml")]
    [InlineData("appcast_beta_x86.xml")]
    [InlineData("appcast_beta_arm64.xml")]
    public void AppcastFile_Exists(string fileName)
    {
        Assert.True(
            File.Exists(GetAppcastPath(fileName)),
            $"Missing appcast file: appcast/{fileName}. All 8 appcast files must be present in the repo.");
    }

    // ── Default appcast must be identical to x64 variant ─────────────────────
    // The arch-neutral file (appcast.xml / appcast_beta.xml) is a copy of the x64 feed.
    // If they diverge, x64 users on the default update URL receive stale data.
    [Theory]
    [InlineData("appcast.xml", "appcast_x64.xml")]
    [InlineData("appcast_beta.xml", "appcast_beta_x64.xml")]
    public void DefaultAppcast_IsIdenticalToX64Variant(string defaultFile, string x64File)
    {
        var defaultContent = File.ReadAllText(GetAppcastPath(defaultFile));
        var x64Content = File.ReadAllText(GetAppcastPath(x64File));
        Assert.True(
            string.Equals(x64Content, defaultContent, StringComparison.Ordinal),
            $"{defaultFile} must be byte-for-byte identical to {x64File}. " +
            "generate-appcast.sh copies the x64 file to produce the default feed.");
    }

    // ── Enclosure URL contains correct architecture ───────────────────────────
    [Theory]
    [InlineData("appcast.xml", "x64")]
    [InlineData("appcast_x64.xml", "x64")]
    [InlineData("appcast_x86.xml", "x86")]
    [InlineData("appcast_arm64.xml", "arm64")]
    [InlineData("appcast_beta.xml", "x64")]
    [InlineData("appcast_beta_x64.xml", "x64")]
    [InlineData("appcast_beta_x86.xml", "x86")]
    [InlineData("appcast_beta_arm64.xml", "arm64")]
    public void AppcastFile_EnclosureUrl_ContainsCorrectArchitecture(string fileName, string arch)
    {
        var url = LoadEnclosure(fileName).Attribute("url")?.Value ?? string.Empty;
        Assert.Contains(
            $"_win-{arch}.exe",
            url,
            StringComparison.Ordinal);
    }

    // ── Enclosure URL starts with the expected GitHub base ────────────────────
    [Theory]
    [InlineData("appcast.xml")]
    [InlineData("appcast_x64.xml")]
    [InlineData("appcast_x86.xml")]
    [InlineData("appcast_arm64.xml")]
    [InlineData("appcast_beta.xml")]
    [InlineData("appcast_beta_x64.xml")]
    [InlineData("appcast_beta_x86.xml")]
    [InlineData("appcast_beta_arm64.xml")]
    public void AppcastFile_EnclosureUrl_StartsWithExpectedBase(string fileName)
    {
        var url = LoadEnclosure(fileName).Attribute("url")?.Value ?? string.Empty;
        Assert.StartsWith(
            "https://github.com/rygel/AIUsageTracker/releases/download/v",
            url,
            StringComparison.Ordinal);
    }

    // ── Enclosure URL version matches item <title> ────────────────────────────
    [Theory]
    [InlineData("appcast.xml")]
    [InlineData("appcast_x64.xml")]
    [InlineData("appcast_x86.xml")]
    [InlineData("appcast_arm64.xml")]
    [InlineData("appcast_beta.xml")]
    [InlineData("appcast_beta_x64.xml")]
    [InlineData("appcast_beta_x86.xml")]
    [InlineData("appcast_beta_arm64.xml")]
    public void AppcastFile_EnclosureUrlVersion_MatchesItemTitle(string fileName)
    {
        var (item, enclosure) = LoadItemAndEnclosure(fileName);
        var title = item.Element("title")?.Value ?? string.Empty;
        Assert.StartsWith("Version ", title, StringComparison.Ordinal);
        var versionInTitle = title["Version ".Length..];
        var url = enclosure.Attribute("url")?.Value ?? string.Empty;
        Assert.Contains($"/v{versionInTitle}/", url, StringComparison.Ordinal);
    }

    // ── sparkle:releaseNotesLink matches version ──────────────────────────────
    [Theory]
    [InlineData("appcast.xml")]
    [InlineData("appcast_x64.xml")]
    [InlineData("appcast_x86.xml")]
    [InlineData("appcast_arm64.xml")]
    [InlineData("appcast_beta.xml")]
    [InlineData("appcast_beta_x64.xml")]
    [InlineData("appcast_beta_x86.xml")]
    [InlineData("appcast_beta_arm64.xml")]
    public void AppcastFile_ReleaseNotesLink_MatchesItemTitleVersion(string fileName)
    {
        var (item, _) = LoadItemAndEnclosure(fileName);
        var releaseNotes = item.Element(Sparkle + "releaseNotesLink")?.Value ?? string.Empty;
        var title = item.Element("title")?.Value ?? string.Empty;
        var versionInTitle = title["Version ".Length..];
        Assert.Equal(
            $"https://github.com/rygel/AIUsageTracker/releases/tag/v{versionInTitle}",
            releaseNotes);
    }

    // ── Beta: enclosure length must be non-zero ───────────────────────────────
    // Regression guard: length="0" breaks download-size display and may cause some
    // clients to reject the update. The CI pipeline populates this from the actual
    // installer file size via INSTALLER_SIZE_* env vars in generate-appcast.sh.
    //
    // If this test fails it means the beta appcast was committed without running the
    // full publish pipeline — copy the real installer byte count into the appcast or
    // re-run scripts/generate-appcast.sh with the INSTALLER_SIZE_* env vars set.
    [Theory]
    [InlineData("appcast_beta.xml")]
    [InlineData("appcast_beta_x64.xml")]
    [InlineData("appcast_beta_x86.xml")]
    [InlineData("appcast_beta_arm64.xml")]
    public void BetaAppcastFile_InstallerLength_IsNonZero(string fileName)
    {
        var enclosure = LoadEnclosure(fileName);
        var lengthStr = enclosure.Attribute("length")?.Value;
        Assert.False(
            string.IsNullOrEmpty(lengthStr),
            $"Missing 'length' attribute in {fileName}.");
        Assert.True(
            long.TryParse(lengthStr, System.Globalization.CultureInfo.InvariantCulture, out var length),
            $"Non-numeric 'length' attribute in {fileName}: '{lengthStr}'.");
        Assert.True(
            length > 0,
            $"enclosure length must be > 0 in {fileName} (got {length}). " +
            "Set INSTALLER_SIZE_X64 / INSTALLER_SIZE_X86 / INSTALLER_SIZE_ARM64 env vars " +
            "when calling scripts/generate-appcast.sh, or populate the value manually.");
    }

    // ── Beta: sparkle:version uses 4-part numeric format ─────────────────────
    // NetSparkle requires a strictly-increasing version number for update detection.
    // Beta releases encode the pre-release suffix as a 4th component so that
    // beta.8 (→ 2.3.4.8) compares as greater than beta.7 (→ 2.3.4.7).
    [Theory]
    [InlineData("appcast_beta.xml")]
    [InlineData("appcast_beta_x64.xml")]
    [InlineData("appcast_beta_x86.xml")]
    [InlineData("appcast_beta_arm64.xml")]
    public void BetaAppcastFile_SparkleVersion_IsFourPartNumeric(string fileName)
    {
        var sparkleVersion = LoadEnclosure(fileName).Attribute(Sparkle + "version")?.Value ?? string.Empty;
        Assert.False(string.IsNullOrEmpty(sparkleVersion), $"Missing sparkle:version in {fileName}.");
        var parts = sparkleVersion.Split('.');
        Assert.Equal(4, parts.Length);
        foreach (var part in parts)
        {
            Assert.True(
                int.TryParse(part, System.Globalization.CultureInfo.InvariantCulture, out _),
                $"Non-numeric component '{part}' in sparkle:version '{sparkleVersion}' in {fileName}.");
        }
    }

    // ── Beta: sparkle:shortVersionString contains -beta. ─────────────────────
    [Theory]
    [InlineData("appcast_beta.xml")]
    [InlineData("appcast_beta_x64.xml")]
    [InlineData("appcast_beta_x86.xml")]
    [InlineData("appcast_beta_arm64.xml")]
    public void BetaAppcastFile_ShortVersionString_ContainsBetaLabel(string fileName)
    {
        var shortVersion = LoadEnclosure(fileName).Attribute(Sparkle + "shortVersionString")?.Value ?? string.Empty;
        Assert.Contains("-beta.", shortVersion, StringComparison.Ordinal);
    }

    // ── Beta: sparkle:version is derived consistently from shortVersionString ─
    // e.g. shortVersionString="2.3.4-beta.11" → sparkle:version must be "2.3.4.11"
    [Theory]
    [InlineData("appcast_beta.xml")]
    [InlineData("appcast_beta_x64.xml")]
    [InlineData("appcast_beta_x86.xml")]
    [InlineData("appcast_beta_arm64.xml")]
    public void BetaAppcastFile_SparkleVersion_IsConsistentWithShortVersionString(string fileName)
    {
        var enclosure = LoadEnclosure(fileName);
        var sparkleVersion = enclosure.Attribute(Sparkle + "version")?.Value ?? string.Empty;
        var shortVersion = enclosure.Attribute(Sparkle + "shortVersionString")?.Value ?? string.Empty;

        const string betaMarker = "-beta.";
        var betaIdx = shortVersion.IndexOf(betaMarker, StringComparison.Ordinal);
        Assert.True(betaIdx >= 0, $"shortVersionString '{shortVersion}' must contain '{betaMarker}' in {fileName}.");

        var coreVersion = shortVersion[..betaIdx];
        var betaNumber = shortVersion[(betaIdx + betaMarker.Length)..];
        var expectedSparkleVersion = $"{coreVersion}.{betaNumber}";

        Assert.True(
            string.Equals(expectedSparkleVersion, sparkleVersion, StringComparison.Ordinal),
            $"sparkle:version in {fileName} must equal '{expectedSparkleVersion}' " +
            $"(derived from shortVersionString '{shortVersion}') but got '{sparkleVersion}'.");
    }

    // ── All beta files reference the same version ─────────────────────────────
    // If the arch-specific files disagree on version, some users would receive the
    // wrong installer or be stuck on the wrong version depending on their architecture.
    [Fact]
    public void BetaAppcastFiles_AllReferenceTheSameVersion()
    {
        string[] betaFiles = ["appcast_beta.xml", "appcast_beta_x64.xml", "appcast_beta_x86.xml", "appcast_beta_arm64.xml"];
        var versions = betaFiles
            .Select(f => LoadEnclosure(f).Attribute(Sparkle + "shortVersionString")?.Value ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.False(versions.Any(v => string.IsNullOrEmpty(v)), "One or more beta appcast files has no sparkle:shortVersionString.");
        Assert.Single(versions);
    }

    // ── All stable files reference the same version ───────────────────────────
    [Fact]
    public void StableAppcastFiles_AllReferenceTheSameVersion()
    {
        string[] stableFiles = ["appcast.xml", "appcast_x64.xml", "appcast_x86.xml", "appcast_arm64.xml"];

        // Stable files may omit sparkle:shortVersionString; fall back to item <title>.
        var versions = stableFiles
            .Select(f =>
            {
                var (item, enclosure) = LoadItemAndEnclosure(f);
                return enclosure.Attribute(Sparkle + "shortVersionString")?.Value
                    ?? item.Element("title")?.Value?.Replace("Version ", string.Empty, StringComparison.Ordinal)
                    ?? string.Empty;
            })
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.False(versions.Any(v => string.IsNullOrEmpty(v)), "One or more stable appcast files has no version.");
        Assert.Single(versions);
    }

    // ── sparkle:os must be "windows" ─────────────────────────────────────────
    [Theory]
    [InlineData("appcast.xml")]
    [InlineData("appcast_x64.xml")]
    [InlineData("appcast_x86.xml")]
    [InlineData("appcast_arm64.xml")]
    [InlineData("appcast_beta.xml")]
    [InlineData("appcast_beta_x64.xml")]
    [InlineData("appcast_beta_x86.xml")]
    [InlineData("appcast_beta_arm64.xml")]
    public void AppcastFile_SparkleOs_IsWindows(string fileName)
    {
        Assert.Equal("windows", LoadEnclosure(fileName).Attribute(Sparkle + "os")?.Value);
    }

    // ── type must be application/octet-stream ─────────────────────────────────
    [Theory]
    [InlineData("appcast.xml")]
    [InlineData("appcast_x64.xml")]
    [InlineData("appcast_x86.xml")]
    [InlineData("appcast_arm64.xml")]
    [InlineData("appcast_beta.xml")]
    [InlineData("appcast_beta_x64.xml")]
    [InlineData("appcast_beta_x86.xml")]
    [InlineData("appcast_beta_arm64.xml")]
    public void AppcastFile_Type_IsOctetStream(string fileName)
    {
        Assert.Equal("application/octet-stream", LoadEnclosure(fileName).Attribute("type")?.Value);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static string GetAppcastPath(string fileName) =>
        Path.Combine(RepoRoot, "appcast", fileName);

    private static XElement LoadEnclosure(string fileName) =>
        LoadItemAndEnclosure(fileName).Enclosure;

    private static (XElement Item, XElement Enclosure) LoadItemAndEnclosure(string fileName)
    {
        var path = GetAppcastPath(fileName);
        var doc = XDocument.Load(path);
        var item = doc.Root?.Element("channel")?.Element("item");
        Assert.NotNull(item);
        var enclosure = item!.Element("enclosure");
        Assert.NotNull(enclosure);
        return (item!, enclosure!);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root — no Directory.Build.props found in any parent directory.");
    }
}
