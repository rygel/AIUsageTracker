// <copyright file="GitHubUpdateChecker.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using AIUsageTracker.Core.Models;
using Microsoft.Extensions.Logging;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.SignatureVerifiers;

namespace AIUsageTracker.Infrastructure.Services;

public class GitHubUpdateChecker
{
#pragma warning disable S1075 // URIs are repository constants
    private const string RepositoryBaseUrl = "https://github.com/rygel/AIUsageTracker";
    private const string RepositoryApiBaseUrl = "https://api.github.com/repos/rygel/AIUsageTracker";
    private const string ArchArm64 = "arm64";
    private const string ArchX64 = "x64";
    private const string ArchX86 = "x86";
    private const string ArchArm = "arm";
#pragma warning restore S1075

    private readonly ILogger<GitHubUpdateChecker> _logger;
    private readonly HttpClient _httpClient;
    private readonly UpdateChannel _channel;

    public GitHubUpdateChecker(ILogger<GitHubUpdateChecker> logger, HttpClient httpClient, UpdateChannel channel = UpdateChannel.Stable)
    {
        this._logger = logger;
        this._httpClient = httpClient;
        this._channel = channel;

        if (this._httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
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
        ArgumentNullException.ThrowIfNull(architecture);

        var normalizedArchitecture = architecture.ToLowerInvariant() switch
        {
            ArchArm => ArchArm64,
            ArchArm64 => ArchArm64,
            ArchX86 => ArchX86,
            _ => ArchX64,
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

            if (updateInfo?.Updates?.Count > 0)
            {
                var latest = updateInfo.Updates[0];

                var latestVersionStr = latest.Version?.TrimStart('v') ?? "0.0.0";
                var currentVersionStr = GetCurrentInformationalVersion();

                if (IsNewerVersion(latestVersionStr, currentVersionStr))
                {
                    this._logger.LogInformation(
                        "New version available: {LatestVersion} (Current: {CurrentVersion})",
                        latestVersionStr,
                        currentVersionStr);

                    var releaseNotes = await this.FetchReleaseNotesFromGitHubAsync(latestVersionStr).ConfigureAwait(false);

                    return new AIUsageTracker.Core.Interfaces.UpdateInfo
                    {
                        Version = latest.Version ?? latestVersionStr,
                        ReleaseUrl = latest.ReleaseNotesLink ?? GetReleaseTagUrl(latestVersionStr),
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
        ArgumentNullException.ThrowIfNull(updateInfo);
        var attemptId = Guid.NewGuid().ToString("N");

        try
        {
            if (string.IsNullOrEmpty(updateInfo.DownloadUrl))
            {
                return UpdateInstallResult.Fail("No download URL available.", attemptId);
            }

            var downloadPath = GetInstallerDownloadPath(updateInfo.Version);
            this._logger.LogInformation(
                "Update attempt {AttemptId}: downloading version {Version} from {Url} to {Path}",
                attemptId,
                updateInfo.Version,
                updateInfo.DownloadUrl,
                downloadPath);
            var downloadSucceeded = await this.DownloadInstallerAsync(updateInfo.DownloadUrl, downloadPath, progress).ConfigureAwait(false);
            if (!downloadSucceeded)
            {
                var exists = File.Exists(downloadPath);
                var length = exists ? new FileInfo(downloadPath).Length : 0;
                return UpdateInstallResult.Fail(
                    $"Download failed — expected installer missing or invalid. AttemptId={attemptId}, Path={downloadPath}, Exists={exists}, SizeBytes={length}.",
                    attemptId,
                    downloadPath);
            }

            var installerHash = ComputeFileSha256(downloadPath);
            var installerSize = new FileInfo(downloadPath).Length;
            this._logger.LogInformation(
                "Update attempt {AttemptId}: download complete. Path={Path}, SizeBytes={SizeBytes}, Sha256={Sha256}",
                attemptId,
                downloadPath,
                installerSize,
                installerHash);

            this._logger.LogInformation("Update attempt {AttemptId}: launching installer from {Path}", attemptId, downloadPath);
            if (!this.StartInstaller(downloadPath, attemptId, out var launchFailureReason))
            {
                return UpdateInstallResult.Fail(launchFailureReason, attemptId, downloadPath, installerHash);
            }

            return UpdateInstallResult.Ok(attemptId, downloadPath, installerHash);
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            this._logger.LogError(ex, "Update attempt {AttemptId}: HTTP error during update download", attemptId);
            return UpdateInstallResult.Fail($"HTTP error (AttemptId={attemptId}): {ex.StatusCode} — {ex.Message}", attemptId);
        }
        catch (TaskCanceledException ex)
        {
            this._logger.LogError(ex, "Update attempt {AttemptId}: download timed out", attemptId);
            return UpdateInstallResult.Fail(
                $"Download timed out while fetching installer (AttemptId={attemptId}) from {updateInfo.DownloadUrl}: {ex.Message}",
                attemptId);
        }
        catch (System.IO.IOException ex)
        {
            this._logger.LogError(ex, "Update attempt {AttemptId}: file system error during update", attemptId);
            return UpdateInstallResult.Fail(
                $"File error while preparing installer for launch (AttemptId={attemptId}): {ex.Message}",
                attemptId);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            this._logger.LogError(ex, "Update attempt {AttemptId}: error during download/install", attemptId);
            return UpdateInstallResult.Fail($"{ex.GetType().Name} (AttemptId={attemptId}): {ex.Message}", attemptId);
        }
    }

    public readonly record struct UpdateInstallResult(
        bool Success,
        string FailureReason,
        string AttemptId,
        string? InstallerPath = null,
        string? InstallerSha256 = null)
    {
        public static UpdateInstallResult Ok(string attemptId, string installerPath, string installerSha256) =>
            new(Success: true, FailureReason: string.Empty, AttemptId: attemptId, InstallerPath: installerPath, InstallerSha256: installerSha256);

        public static UpdateInstallResult Fail(string reason, string attemptId, string? installerPath = null, string? installerSha256 = null) =>
            new(Success: false, FailureReason: reason, AttemptId: attemptId, InstallerPath: installerPath, InstallerSha256: installerSha256);
    }

    /// <summary>
    /// Parses an app version string of the form "M.m.p-beta.N" or "M.m.p" into a comparable
    /// tuple. Stable releases sort higher than any pre-release of the same core version.
    /// </summary>
    /// <returns></returns>
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
    /// <returns></returns>
    internal static bool IsNewerVersion(string candidate, string current)
    {
        return ParseAppVersion(candidate.TrimStart('v')).CompareTo(ParseAppVersion(current.TrimStart('v'))) > 0;
    }

    /// <summary>
    /// Returns the running app's informational version (e.g. "2.3.4-beta.7"), stripping any
    /// build-metadata suffix appended by the SDK (e.g. "+abc1234").
    /// </summary>
    /// <returns></returns>
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
        this._logger.LogDebug("Checking for beta/stable updates via GitHub Releases API");

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

        AIUsageTracker.Core.Interfaces.UpdateInfo? bestUpdate = null;
        var bestVersionStr = currentVersionStr;

        foreach (var release in doc.RootElement.EnumerateArray())
        {
            var tagName = release.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
            if (string.IsNullOrWhiteSpace(tagName))
            {
                continue;
            }

            var versionStr = tagName.TrimStart('v');

            if (!IsNewerVersion(versionStr, bestVersionStr))
            {
                continue;
            }

            var update = this.TryParseRelease(release, versionStr);
            if (update != null)
            {
                bestUpdate = update;
                bestVersionStr = versionStr;
            }
        }

        if (bestUpdate != null)
        {
            this._logger.LogInformation("Update available for beta channel: {Version}", bestVersionStr);
        }
        else
        {
            this._logger.LogDebug("No updates available for beta channel");
        }

        return bestUpdate;
    }

    private AIUsageTracker.Core.Interfaces.UpdateInfo? TryParseRelease(JsonElement release, string versionStr)
    {
        var publishedAt = release.TryGetProperty("published_at", out var pub) &&
                          DateTime.TryParse(pub.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt
            : DateTime.UtcNow;

        var releaseBody = release.TryGetProperty("body", out var body)
            ? body.GetString() ?? string.Empty
            : string.Empty;

        var tagName = release.TryGetProperty("tag_name", out var tag) ? tag.GetString() : $"v{versionStr}";

        var arch = GetCurrentArchitectureName();
        var downloadUrl = $"{RepositoryBaseUrl}/releases/download/{tagName}/AIUsageTracker_Setup_v{versionStr}_win-{arch}.exe";

        return new AIUsageTracker.Core.Interfaces.UpdateInfo
        {
            Version = versionStr,
            ReleaseUrl = GetReleaseTagUrl(versionStr),
            DownloadUrl = downloadUrl,
            ReleaseNotes = releaseBody,
            PublishedAt = publishedAt,
        };
    }

    private static string GetInstallerDownloadPath(string version)
    {
        var updatesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIUsageTracker",
            "Updates");
        Directory.CreateDirectory(updatesDir);
        return Path.Combine(updatesDir, $"AIUsageTracker_Setup_{version}.exe");
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string GetCurrentArchitectureName()
    {
        return System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture switch
        {
            System.Runtime.InteropServices.Architecture.Arm64 => ArchArm64,
            System.Runtime.InteropServices.Architecture.Arm => ArchArm64,
            System.Runtime.InteropServices.Architecture.X86 => ArchX86,
            _ => ArchX64,
        };
    }

    private string GetAppcastUrlForCurrentArchitecture()
    {
        var currentArch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLower(System.Globalization.CultureInfo.InvariantCulture);

        var archMapping = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ArchX64] = ArchX64,
            [ArchX86] = ArchX86,
            [ArchArm64] = ArchArm64,
            [ArchArm] = ArchArm64,
        };

        var targetArch = archMapping.GetValueOrDefault(currentArch, ArchX64);

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

        var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        using (var fileStream = new FileStream(partialDownloadPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
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

    private bool StartInstaller(string installerPath, string attemptId, out string failureReason)
    {
        this._logger.LogInformation("Update attempt {AttemptId}: starting installer from {Path}", attemptId, installerPath);
        failureReason = string.Empty;

        if (!File.Exists(installerPath))
        {
            failureReason = $"Installer launch failed (AttemptId={attemptId}): file not found at {installerPath}.";
            this._logger.LogError("Update attempt {AttemptId}: installer launch aborted because file does not exist: {Path}", attemptId, installerPath);
            return false;
        }

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
                this._logger.LogError("Update attempt {AttemptId}: installer launch returned no process for {Path}", attemptId, installerPath);
                failureReason = $"Installer launch returned no process (AttemptId={attemptId}). Path={installerPath}.";
                return false;
            }

            this._logger.LogInformation("Update attempt {AttemptId}: installer started successfully from {Path} (PID {ProcessId}).", attemptId, installerPath, process.Id);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            var nativeCode = ex is System.ComponentModel.Win32Exception win32Ex ? win32Ex.NativeErrorCode : 0;
            var message = $"Installer launch failed (AttemptId={attemptId}). Path={installerPath}, ErrorType={ex.GetType().Name}, NativeErrorCode={nativeCode}, Message={ex.Message}";
            this._logger.LogError(ex, "Update attempt {AttemptId}: failed to start installer from {Path}", attemptId, installerPath);
            failureReason = message;
            return false;
        }
    }

    private static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }
}
