// <copyright file="GitHubUpdateChecker.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

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
            // Get the appcast URL for current architecture
            var appcastUrl = this.GetAppcastUrlForCurrentArchitecture();

            // Initialize SparkleUpdater with the architecture-specific appcast URL
            using var sparkle = new SparkleUpdater(appcastUrl, new Ed25519Checker(SecurityMode.Unsafe));

            this._logger.LogDebug("Checking for updates via NetSparkle appcast: {Url}", appcastUrl);

            // Check for updates quietly (no UI)
            var updateInfo = await sparkle.CheckForUpdatesQuietly().ConfigureAwait(false);

            if (updateInfo?.Updates?.Any() == true)
            {
                var latest = updateInfo.Updates.First();
                var currentVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName()?.Version ?? new Version(1, 0, 0);

                // Parse version (handle 'v' prefix)
                var latestVersionStr = latest.Version?.TrimStart('v') ?? "0.0.0";

                if (Version.TryParse(latestVersionStr, out var latestVersion) && latestVersion > currentVersion)
                {
                    this._logger.LogInformation(
                        "New version available: {LatestVersion} (Current: {CurrentVersion})",
                        latestVersion,
                        currentVersion);

                    // Fetch release notes from GitHub API
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
        catch (Exception ex)
        {
            this._logger.LogWarning(ex, "Failed to check for updates via NetSparkle appcast");
            return null;
        }
    }

    public async Task<bool> DownloadAndInstallUpdateAsync(AIUsageTracker.Core.Interfaces.UpdateInfo updateInfo, IProgress<double>? progress = null)
    {
        try
        {
            this._logger.LogInformation("Starting download and install for version {Version}", updateInfo.Version);

            if (string.IsNullOrEmpty(updateInfo.DownloadUrl))
            {
                this._logger.LogWarning("No download URL available for update");
                return false;
            }

            var downloadPath = GetInstallerDownloadPath(updateInfo.Version);
            var downloadSucceeded = await this.DownloadInstallerAsync(updateInfo.DownloadUrl, downloadPath, progress).ConfigureAwait(false);
            if (!downloadSucceeded)
            {
                return false;
            }

            return this.StartInstaller(downloadPath);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error during download and install");
            return false;
        }
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

    private string GetAppcastUrlForCurrentArchitecture()
    {
        var currentArch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLower(System.Globalization.CultureInfo.InvariantCulture);

        // Map architecture names
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

        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var fileStream = new FileStream(partialDownloadPath, FileMode.Create, FileAccess.Write, FileShare.None);

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
            using var doc = System.Text.Json.JsonDocument.Parse(content);

            if (doc.RootElement.TryGetProperty("body", out var bodyElement))
            {
                var releaseNotes = bodyElement.GetString() ?? string.Empty;
                this._logger.LogDebug("Successfully fetched release notes ({Length} chars)", releaseNotes.Length);
                return releaseNotes;
            }

            return string.Empty;
        }
        catch (Exception ex)
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
            Verb = "runas", // Run as administrator
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
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Failed to start installer from {Path}", installerPath);
            return false;
        }
    }
}
