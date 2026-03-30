// <copyright file="UpdateChannelConfigurationEndToEndTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Xml.Linq;
using AIUsageTracker.Infrastructure.Services;
using AIUsageTracker.Tests.Infrastructure;

namespace AIUsageTracker.Tests.Core;

public sealed class UpdateChannelConfigurationEndToEndTests : IDisposable
{
    private readonly string _tempRoot;

    public UpdateChannelConfigurationEndToEndTests()
    {
        this._tempRoot = TestTempPaths.CreateDirectory("update-channel-e2e-tests");
    }

    [Theory]
    [InlineData("2.2.28", false, "AI Usage Tracker")]
    [InlineData("2.2.28-beta.21", true, "AI Usage Tracker (Beta Channel)")]
    public async Task GenerateAppcastScript_CreatesChannelFeedsThatMatchRuntimeUrlsAsync(
        string version,
        bool isBeta,
        string expectedTitle)
    {
        var workingDirectory = CreateScriptWorkspace(this._tempRoot);

        var generated = await RunGenerateAppcastAsync(workingDirectory, version, isBeta ? "beta" : "stable");
        if (!generated)
        {
            return;
        }

        var prefix = isBeta ? "appcast_beta" : "appcast";
        var defaultFile = Path.Combine(workingDirectory, "appcast", $"{prefix}.xml");
        var x64File = Path.Combine(
            workingDirectory,
            "appcast",
            Path.GetFileName(new Uri(GitHubUpdateChecker.GetAppcastUrl("x64", isBeta)).AbsolutePath));
        var x86File = Path.Combine(
            workingDirectory,
            "appcast",
            Path.GetFileName(new Uri(GitHubUpdateChecker.GetAppcastUrl("x86", isBeta)).AbsolutePath));
        var arm64File = Path.Combine(
            workingDirectory,
            "appcast",
            Path.GetFileName(new Uri(GitHubUpdateChecker.GetAppcastUrl("arm64", isBeta)).AbsolutePath));

        Assert.True(File.Exists(defaultFile), $"Missing generated appcast file: {defaultFile}");
        Assert.True(File.Exists(x64File), $"Missing generated x64 appcast file: {x64File}");
        Assert.True(File.Exists(x86File), $"Missing generated x86 appcast file: {x86File}");
        Assert.True(File.Exists(arm64File), $"Missing generated arm64 appcast file: {arm64File}");
        Assert.Equal(await File.ReadAllTextAsync(defaultFile), await File.ReadAllTextAsync(x64File));

        AssertFeed(
            defaultFile,
            expectedTitle,
            version,
            BuildExpectedDownloadUrl(version, "x64"),
            version);
        AssertFeed(
            x86File,
            $"{expectedTitle} - x86",
            version,
            BuildExpectedDownloadUrl(version, "x86"),
            version);
        AssertFeed(
            arm64File,
            $"{expectedTitle} - ARM64",
            version,
            BuildExpectedDownloadUrl(version, "arm64"),
            version);
    }

    // ── Installer size is populated from INSTALLER_SIZE_* env vars ───────────
    // Regression guard: if env vars are provided (as they are in the CI publish pipeline),
    // the generated enclosure length must equal the supplied size for every architecture.
    // length="0" in a committed appcast means the publish pipeline failed to pass sizes.

    [Theory]
    [InlineData("2.2.28",       "stable", 10_000_000L, 9_500_000L,  9_800_000L)]
    [InlineData("2.2.28-beta.5", "beta",  19_451_188L, 18_831_779L, 18_802_141L)]
    public async Task GenerateAppcastScript_PopulatesInstallerLengthFromEnvVarsAsync(
        string version, string channel, long x64Size, long x86Size, long arm64Size)
    {
        var workingDirectory = CreateScriptWorkspace(this._tempRoot);

        var envVars = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["INSTALLER_SIZE_X64"] = x64Size.ToString(),
            ["INSTALLER_SIZE_X86"] = x86Size.ToString(),
            ["INSTALLER_SIZE_ARM64"] = arm64Size.ToString(),
        };

        var generated = await RunGenerateAppcastAsync(workingDirectory, version, channel, envVars);
        if (!generated)
        {
            return;
        }

        var prefix = channel == "beta" ? "appcast_beta" : "appcast";
        AssertInstallerLength(Path.Combine(workingDirectory, "appcast", $"{prefix}.xml"), x64Size);
        AssertInstallerLength(Path.Combine(workingDirectory, "appcast", $"{prefix}_x64.xml"), x64Size);
        AssertInstallerLength(Path.Combine(workingDirectory, "appcast", $"{prefix}_x86.xml"), x86Size);
        AssertInstallerLength(Path.Combine(workingDirectory, "appcast", $"{prefix}_arm64.xml"), arm64Size);
    }

    public void Dispose()
    {
        TestTempPaths.CleanupPath(this._tempRoot);
    }

    private static void AssertInstallerLength(string appcastFilePath, long expectedLength)
    {
        Assert.True(File.Exists(appcastFilePath), $"Missing generated appcast: {appcastFilePath}");
        var document = XDocument.Load(appcastFilePath);
        XNamespace sparkle = "http://www.andymatuschak.org/xml-namespaces/sparkle";
        var enclosure = document.Root?.Element("channel")?.Element("item")?.Element("enclosure");
        Assert.NotNull(enclosure);
        var lengthStr = enclosure!.Attribute("length")?.Value;
        Assert.False(string.IsNullOrEmpty(lengthStr), $"Missing length attribute in {appcastFilePath}.");
        Assert.True(long.TryParse(lengthStr, out var actualLength), $"Non-numeric length '{lengthStr}' in {appcastFilePath}.");
        Assert.Equal(expectedLength, actualLength);
    }

    private static void AssertFeed(
        string filePath,
        string expectedTitle,
        string version,
        string expectedDownloadUrl,
        string expectedShortVersion)
    {
        var document = XDocument.Load(filePath);
        XNamespace sparkle = "http://www.andymatuschak.org/xml-namespaces/sparkle";

        var channel = document.Root?.Element("channel");
        Assert.NotNull(channel);

        var item = channel!.Element("item");
        Assert.NotNull(item);

        var enclosure = item!.Element("enclosure");
        Assert.NotNull(enclosure);

        Assert.Equal(expectedTitle, channel.Element("title")?.Value);
        Assert.Equal($"Version {version}", item.Element("title")?.Value);
        Assert.Equal(
            GitHubUpdateChecker.GetReleaseTagUrl(version),
            item.Element(sparkle + "releaseNotesLink")?.Value);
        Assert.Equal(expectedDownloadUrl, enclosure!.Attribute("url")?.Value);
        // For beta releases (e.g. "2.2.28-beta.21") the script encodes the pre-release number
        // as a 4th component ("2.2.28.21") so NetSparkle can compare builds numerically.
        const string betaPrefix = "-beta.";
        var betaIdx = version.IndexOf(betaPrefix, StringComparison.Ordinal);
        var expectedSparkleVersion = betaIdx >= 0
            ? $"{version[..betaIdx]}.{version[(betaIdx + betaPrefix.Length)..]}"
            : version;
        Assert.Equal(expectedSparkleVersion, enclosure.Attribute(sparkle + "version")?.Value);
        Assert.Equal(expectedShortVersion, enclosure.Attribute(sparkle + "shortVersionString")?.Value);
    }

    private static Task<bool> RunGenerateAppcastAsync(string workingDirectory, string version, string channel)
        => RunGenerateAppcastAsync(workingDirectory, version, channel, envVars: null);

    private static async Task<bool> RunGenerateAppcastAsync(
        string workingDirectory,
        string version,
        string channel,
        IReadOnlyDictionary<string, string>? envVars)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "bash",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("./scripts/generate-appcast.sh");
        startInfo.ArgumentList.Add(version);
        startInfo.ArgumentList.Add(channel);

        if (envVars != null)
        {
            foreach (var (key, value) in envVars)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);

        var stdoutTask = process!.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0 && IsKnownLocalBashResourceFailure(stdout, stderr))
        {
            return false;
        }

        Assert.True(
            process.ExitCode == 0,
            $"generate-appcast.sh failed with exit code {process.ExitCode}.{Environment.NewLine}STDOUT:{Environment.NewLine}{stdout}{Environment.NewLine}STDERR:{Environment.NewLine}{stderr}");
        return true;
    }

    private static string CreateScriptWorkspace(string tempRoot)
    {
        var workingDirectory = Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(workingDirectory, "scripts"));

        var repoRoot = GetRepoRoot();
        var sourceScriptPath = Path.Combine(repoRoot, "scripts", "generate-appcast.sh");
        var destinationScriptPath = Path.Combine(workingDirectory, "scripts", "generate-appcast.sh");
        var scriptContent = File.ReadAllText(sourceScriptPath).Replace("\r\n", "\n", StringComparison.Ordinal);
        File.WriteAllText(destinationScriptPath, scriptContent);

        return workingDirectory;
    }

    private static bool IsKnownLocalBashResourceFailure(string stdout, string stderr)
    {
        var normalizedOutput = NormalizeOutput($"{stdout}\n{stderr}");
        return ContainsAny(
            normalizedOutput,
            "0x800705aa",
            "Bash/Service/CreateInstance/CreateVm/HCS",
            "Insufficient system resources exist to complete the requested service",
            "Windows Subsystem for Linux has no installed distributions");
    }

    private static string NormalizeOutput(string output)
    {
        return output.Replace("\0", string.Empty, StringComparison.Ordinal);
    }

    private static bool ContainsAny(string source, params string[] markers)
    {
        foreach (var marker in markers)
        {
            if (source.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildExpectedDownloadUrl(string version, string architecture)
    {
        return $"{GitHubUpdateChecker.GetReleasesPageUrl()}/download/v{version}/AIUsageTracker_Setup_v{version}_win-{architecture}.exe";
    }

    private static string GetRepoRoot()
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

        throw new InvalidOperationException("Could not find repo root.");
    }
}
