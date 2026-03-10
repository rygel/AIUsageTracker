// <copyright file="UpdateChannelConfigurationEndToEndTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Diagnostics;
using System.Xml.Linq;
using AIUsageTracker.Core.Updates;

namespace AIUsageTracker.Tests.Core;

public sealed class UpdateChannelConfigurationEndToEndTests : IDisposable
{
    private readonly string _tempRoot;

    public UpdateChannelConfigurationEndToEndTests()
    {
        this._tempRoot = Path.Combine(
            Path.GetTempPath(),
            "update-channel-e2e-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this._tempRoot);
    }

    [Theory]
    [InlineData("2.2.28", false, "AI Usage Tracker")]
    [InlineData("2.2.28-beta.21", true, "AI Usage Tracker (Beta Channel)")]
    public async Task GenerateAppcastScript_CreatesChannelFeedsThatMatchRuntimeUrlsAsync(
        string version,
        bool isBeta,
        string expectedTitle)
    {
        var workingDirectory = this.CreateScriptWorkspace();

        await RunGenerateAppcastAsync(workingDirectory, version, isBeta ? "beta" : "stable");

        var prefix = isBeta ? "appcast_beta" : "appcast";
        var defaultFile = Path.Combine(workingDirectory, "appcast", $"{prefix}.xml");
        var x64File = Path.Combine(
            workingDirectory,
            "appcast",
            Path.GetFileName(new Uri(ReleaseUrlCatalog.GetAppcastUrl("x64", isBeta)).AbsolutePath));
        var x86File = Path.Combine(
            workingDirectory,
            "appcast",
            Path.GetFileName(new Uri(ReleaseUrlCatalog.GetAppcastUrl("x86", isBeta)).AbsolutePath));
        var arm64File = Path.Combine(
            workingDirectory,
            "appcast",
            Path.GetFileName(new Uri(ReleaseUrlCatalog.GetAppcastUrl("arm64", isBeta)).AbsolutePath));

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

    public void Dispose()
    {
        if (Directory.Exists(this._tempRoot))
        {
            Directory.Delete(this._tempRoot, recursive: true);
        }
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
            ReleaseUrlCatalog.GetReleaseTagUrl(version),
            item.Element(sparkle + "releaseNotesLink")?.Value);
        Assert.Equal(expectedDownloadUrl, enclosure!.Attribute("url")?.Value);
        Assert.Equal(version.Split('-')[0], enclosure.Attribute(sparkle + "version")?.Value);
        Assert.Equal(expectedShortVersion, enclosure.Attribute(sparkle + "shortVersionString")?.Value);
    }

    private string CreateScriptWorkspace()
    {
        var workingDirectory = Path.Combine(this._tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(workingDirectory, "scripts"));

        var repoRoot = GetRepoRoot();
        var sourceScriptPath = Path.Combine(repoRoot, "scripts", "generate-appcast.sh");
        var destinationScriptPath = Path.Combine(workingDirectory, "scripts", "generate-appcast.sh");
        var scriptContent = File.ReadAllText(sourceScriptPath).Replace("\r\n", "\n", StringComparison.Ordinal);
        File.WriteAllText(destinationScriptPath, scriptContent);

        return workingDirectory;
    }

    private static async Task RunGenerateAppcastAsync(string workingDirectory, string version, string channel)
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

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);

        var stdoutTask = process!.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        Assert.True(
            process.ExitCode == 0,
            $"generate-appcast.sh failed with exit code {process.ExitCode}.{Environment.NewLine}STDOUT:{Environment.NewLine}{stdout}{Environment.NewLine}STDERR:{Environment.NewLine}{stderr}");
    }

    private static string BuildExpectedDownloadUrl(string version, string architecture)
    {
        return $"{ReleaseUrlCatalog.GetReleasesPageUrl()}/download/v{version}/AIUsageTracker_Setup_v{version}_win-{architecture}.exe";
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
