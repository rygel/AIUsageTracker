// <copyright file="GitHubUpdateChecker.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using Microsoft.Extensions.Logging;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.SignatureVerifiers;

namespace AIUsageTracker.Infrastructure.Services;

public class GitHubUpdateChecker
{
    private const string RepositoryBaseUrl = "https://github.com/rygel/AIUsageTracker";
    private const string RepositoryApiBaseUrl = "https://api.github.com/repos/rygel/AIUsageTracker";

    private readonly ILogger<GitHubUpdateChecker> _logger;
    private readonly HttpClient _httpClient;
    private readonly UpdateChannel _channel;

    public GitHubUpdateChecker(ILogger<GitHubUpdateChecker> logger, HttpClient httpClient, UpdateChannel channel = UpdateChannel.Stable)
    {
        this._logger = logger;
        this._httpClient = httpClient;
        this._channel = channel;

        if (!this._httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            this._httpClient.DefaultRequestHeaders.Add("User-Agent", "AIUsageTracker");
        }
    }

    public static string GetReleasesPageUrl()
    {
        return $"{RepositoryBaseUrl}/releases";
    }

    public static string GetLatestReleasePageUrl()
    {
        return $"{GetReleasesPageUrl()}/latest";
    }

    public static string GetReleaseTagUrl(string version)
    {
        return $"{GetReleasesPageUrl()}/tag/v{version}";
    }

    public static string GetGitHubReleaseApiUrl(string version)
    {
        return $"{RepositoryApiBaseUrl}/releases/tags/v{version}";
    }

    public static string GetAppcastUrl(string architecture, bool isBeta)
    {
        var normalizedArchitecture = architecture.ToLowerInvariant() switch
        {
            "arm" => "arm64",
            "arm64" => "arm64",
            "x86" => "x86",
            _ => "x64",
        };

        var appcastName = isBeta
            ? $"appcast_beta_{normalizedArchitecture}.xml"
            : $"appcast_{normalizedArchitecture}.xml";

        return $"{GetReleasesPageUrl()}/latest/download/{appcastName}";
    }

    public async Task<AIUsageTracker.Core.Interfaces.UpdateInfo?> CheckForUpdatesAsync()
    {
        try
        {
            if (this._channel == UpdateChannel.Beta)
            {
                return await this.CheckForBetaUpdatesAsync().ConfigureAwait(false);
            }

            // Stable channel: use NetSparkle appcast
            var appcastUrl = this.GetAppcastUrlForCurrentArchitecture();
            using var sparkle = new SparkleUpdater(appcastUrl, new Ed25519Checker(SecurityMode.Unsafe));

            this._logger.LogDebug("Checking for updates via NetSparkle appcast: {Url}", appcastUrl);

            var updateInfo = await sparkle.CheckForUpdatesQuietly().ConfigureAwait(false);

            if (updateInfo?.Updates?.Any() == true)
            {
                var latest = updateInfo.Updates.First();
                var currentVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName()?.Version ?? new Version(1, 0, 0);

                var latestVersionStr = latest.Version?.TrimStart('v') ?? "0.0.0";

                if (Version.TryParse(latestVersionStr, out var latestVersion) && latestVersion > currentVersion)
                {
                    this._logger.LogInformation(
                        "New version available: {LatestVersion} (Current: {CurrentVersion})",
                        latestVersion,
                        currentVersion);

                    var releaseNotes = await this.FetchReleaseNotesFromGitHubAsync(latestVersionStr).ConfigureAwait(false);

                    return new AIUsageTracker.Core.Interfaces.UpdateInfo
                    {
                        Version = latest.Version ?? latestVersion.ToString(),
                        ReleaseUrl = latest.ReleaseNotesLink ?? GetReleaseTagUrl(latestVersion.ToString()),
                        DownloadUrl = latest.DownloadLink ?? string.Empty,
                        ReleaseNotes = releaseNotes,
                        PublishedAt = latest.PublicationDate,
                    };
                }
            }

            this._logger.LogDebug("No updates available or already on latest version");
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            this._logger.LogWarning(ex, "Failed to check for updates via NetSparkle appcast");
            return null;
        }
    }

    public async Task<UpdateInstallResult> DownloadAndInstallUpdateAsync(AIUsageTracker.Core.Interfaces.UpdateInfo updateInfo, IProgress<double>? progress = null)
    {
        try
        {
            this._logger.LogInformation("Starting download and install for version {Version}", updateInfo.Version);

            if (string.IsNullOrEmpty(updateInfo.DownloadUrl))
            {
                return UpdateInstallResult.Fail("No download URL available.");
            }

            var downloadPath = GetInstallerDownloadPath(updateInfo.Version);
            this._logger.LogInformation("Downloading update from {Url} to {Path}", updateInfo.DownloadUrl, downloadPath);
            var downloadSucceeded = await this.DownloadInstallerAsync(updateInfo.DownloadUrl, downloadPath, progress).ConfigureAwait(false);
            if (!downloadSucceeded)
            {
                return UpdateInstallResult.Fail($"Download failed — file not found at {downloadPath} after transfer.");
            }

            this._logger.LogInformation("Download succeeded ({Path}), launching installer", downloadPath);
            if (!this.StartInstaller(downloadPath))
            {
                return UpdateInstallResult.Fail($"Installer failed to launch from {downloadPath}. Check UAC or antivirus.");
            }

            return UpdateInstallResult.Ok();
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            this._logger.LogError(ex, "HTTP error during update download");
            return UpdateInstallResult.Fail($"HTTP error: {ex.StatusCode} — {ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            this._logger.LogError(ex, "Download timed out");
            return UpdateInstallResult.Fail($"Download timed out: {ex.Message}");
        }
        catch (System.IO.IOException ex)
        {
            this._logger.LogError(ex, "File system error during update");
            return UpdateInstallResult.Fail($"File error: {ex.Message}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            this._logger.LogError(ex, "Error during download and install");
            return UpdateInstallResult.Fail($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    public readonly record struct UpdateInstallResult(bool Success, string FailureReason)
    {
        public static UpdateInstallResult Ok() => new(true, string.Empty);

        public static UpdateInstallResult Fail(string reason) => new(false, reason);
    }

    /// <summary>
    /// Parses an app version string of the form "M.m.p-beta.N" or "M.m.p" into a comparable
    /// tuple. Stable releases sort higher than any pre-release of the same core version.
    /// </summary>
    internal static (int Major, int Minor, int Patch, int PreRelease) ParseAppVersion(string version)
    {
        var betaIndex = version.IndexOf("-beta.", StringComparison.OrdinalIgnoreCase);

        string coreVersion;
        int preRelease;

        if (betaIndex >= 0)
        {
            coreVersion = version[..betaIndex];
            var betaNumStr = version[(betaIndex + 6)..]; // skip "-beta."
            preRelease = int.TryParse(betaNumStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
        }
        else
        {
            coreVersion = version;
            preRelease = int.MaxValue; // stable is higher than any beta of the same core version
        }

        var parts = coreVersion.Split('.');
        var major = parts.Length > 0 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
        var minor = parts.Length > 1 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        var patch = parts.Length > 2 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;

        return (major, minor, patch, preRelease);
    }

    /// <summary>Returns true when <paramref name="candidate"/> is a newer release than <paramref name="current"/>.</summary>
    internal static bool IsNewerVersion(string candidate, string current)
    {
        return ParseAppVersion(candidate.TrimStart('v')).CompareTo(ParseAppVersion(current.TrimStart('v'))) > 0;
    }

    /// <summary>
    /// Returns the running app's informational version (e.g. "2.3.4-beta.7"), stripping any
    /// build-metadata suffix appended by the SDK (e.g. "+abc1234").
    /// </summary>
    internal static string GetCurrentInformationalVersion()
    {
        var raw = System.Reflection.CustomAttributeExtensions
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(
                System.Reflection.Assembly.GetEntryAssembly()!)
            ?.InformationalVersion ?? string.Empty;

        var plusIdx = raw.IndexOf('+', StringComparison.Ordinal);
        return plusIdx >= 0 ? raw[..plusIdx] : raw;
    }

    private async Task<AIUsageTracker.Core.Interfaces.UpdateInfo?> CheckForBetaUpdatesAsync()
    {
        // /releases/latest only resolves to non-pre-release releases, so beta appcasts are
        // never reachable via that URL. Use the GitHub Releases API directly instead.
        this._logger.LogDebug("Checking for beta updates via GitHub Releases API");

        var releasesUrl = $"{RepositoryApiBaseUrl}/releases?per_page=10";
        using var response = await this._httpClient.GetAsync(releasesUrl).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            this._logger.LogWarning("GitHub releases API returned {StatusCode}", response.StatusCode);
            return null;
        }

        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        using var doc = JsonDocument.Parse(content);

        var currentVersionStr = GetCurrentInformationalVersion();

        // Releases are returned newest-first; take the first pre-release that is newer.
        foreach (var release in doc.RootElement.EnumerateArray())
        {
            var isPrerelease = release.TryGetProperty("prerelease", out var pre) && pre.GetBoolean();
            if (!isPrerelease)
            {
                continue;
            }

            var tagName = release.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
            if (string.IsNullOrWhiteSpace(tagName))
            {
                continue;
            }

            var latestVersionStr = tagName.TrimStart('v');

            if (!IsNewerVersion(latestVersionStr, currentVersionStr))
            {
                break; // sorted newest-first; nothing further can be newer
            }

            var publishedAt = release.TryGetProperty("published_at", out var pub) &&
                              DateTime.TryParse(pub.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                ? dt
                : DateTime.UtcNow;

            var releaseBody = release.TryGetProperty("body", out var body)
                ? body.GetString() ?? string.Empty
                : string.Empty;

            var arch = this.GetCurrentArchitectureName();
            var downloadUrl = $"{RepositoryBaseUrl}/releases/download/{tagName}/AIUsageTracker_Setup_v{latestVersionStr}_win-{arch}.exe";

            this._logger.LogInformation("Beta update available: {Version}", latestVersionStr);

            return new AIUsageTracker.Core.Interfaces.UpdateInfo
            {
                Version = latestVersionStr,
                ReleaseUrl = GetReleaseTagUrl(latestVersionStr),
                DownloadUrl = downloadUrl,
                ReleaseNotes = releaseBody,
                PublishedAt = publishedAt,
            };
        }

        this._logger.LogDebug("No beta updates available");
        return null;
    }

    private static string GetInstallerDownloadPath(string version)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AIUsageTracker_Updates");
        Directory.CreateDirectory(tempDir);
        return Path.Combine(tempDir, $"AIUsageTracker_Setup_{version}.exe");
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string GetCurrentArchitectureName()
    {
        return System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            System.Runtime.InteropServices.Architecture.Arm => "arm64",
            System.Runtime.InteropServices.Architecture.X86 => "x86",
            _ => "x64",
        };
    }

    private string GetAppcastUrlForCurrentArchitecture()
    {
        var currentArch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLower(System.Globalization.CultureInfo.InvariantCulture);

        var archMapping = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["x64"] = "x64",
            ["x86"] = "x86",
            ["arm64"] = "arm64",
            ["arm"] = "arm64",
        };

        var targetArch = archMapping.GetValueOrDefault(currentArch, "x64");

        if (!archMapping.ContainsKey(currentArch))
        {
            this._logger.LogWarning("Unknown architecture {Architecture}, falling back to x64", currentArch);
        }

        var url = GetAppcastUrl(targetArch, this._channel == UpdateChannel.Beta);
        this._logger.LogDebug("Using appcast for architecture {Architecture} ({Channel}): {Url}", targetArch, this._channel, url);
        return url;
    }

    private async Task<bool> DownloadInstallerAsync(string downloadUrl, string downloadPath, IProgress<double>? progress)
    {
        var partialDownloadPath = $"{downloadPath}.partial";
        this._logger.LogInformation("Downloading from {Url} to {Path}", downloadUrl, downloadPath);
        DeleteIfExists(downloadPath);
        DeleteIfExists(partialDownloadPath);

        using var response = await this._httpClient.GetAsync(downloadUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        var downloadedBytes = 0L;
        var buffer = new byte[8192];

        using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
        using (var fileStream = new FileStream(partialDownloadPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                downloadedBytes += read;
                if (totalBytes > 0 && progress != null)
                {
                    var percentage = (double)downloadedBytes / totalBytes * 100;
                    progress.Report(percentage);
                }
            }

            await fileStream.FlushAsync().ConfigureAwait(false);
        }

        File.Move(partialDownloadPath, downloadPath, overwrite: true);
        this._logger.LogInformation("Download completed successfully to {Path}", downloadPath);

        if (File.Exists(downloadPath))
        {
            return true;
        }

        this._logger.LogError("Downloaded file not found at {Path}", downloadPath);
        return false;
    }

    private async Task<string> FetchReleaseNotesFromGitHubAsync(string version)
    {
        try
        {
            var url = GetGitHubReleaseApiUrl(version);
            this._logger.LogDebug("Fetching release notes from: {Url}", url);

            using var response = await this._httpClient.GetAsync(url).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                this._logger.LogWarning("Failed to fetch release notes: {StatusCode}", response.StatusCode);
                return string.Empty;
            }

            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(content);

            if (doc.RootElement.TryGetProperty("body", out var bodyElement))
            {
                var releaseNotes = bodyElement.GetString() ?? string.Empty;
                this._logger.LogDebug("Successfully fetched release notes ({Length} chars)", releaseNotes.Length);
                return releaseNotes;
            }

            return string.Empty;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            this._logger.LogWarning(ex, "Failed to fetch release notes from GitHub API");
            return string.Empty;
        }
    }

    private bool StartInstaller(string installerPath)
    {
        this._logger.LogInformation("Starting installer from {Path}", installerPath);

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
            UseShellExecute = true,
        };

        try
        {
            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null)
            {
                this._logger.LogError("Installer launch returned no process for {Path}", installerPath);
                return false;
            }

            this._logger.LogInformation("Installer started successfully from {Path} (PID {ProcessId}).", installerPath, process.Id);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            this._logger.LogError(ex, "Failed to start installer from {Path}", installerPath);
            return false;
        }
    }
}
