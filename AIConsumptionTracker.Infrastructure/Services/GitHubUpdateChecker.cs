using Microsoft.Extensions.Logging;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.SignatureVerifiers;
using AIConsumptionTracker.Core.Interfaces;

namespace AIConsumptionTracker.Infrastructure.Services;

public class GitHubUpdateChecker : IUpdateCheckerService
{
    private readonly ILogger<GitHubUpdateChecker> _logger;
    private const string APPCAST_URL = "https://github.com/rygel/AIConsumptionTracker/releases/latest/download/appcast.xml";

    public GitHubUpdateChecker(ILogger<GitHubUpdateChecker> logger)
    {
        _logger = logger;
    }

    public async Task<AIConsumptionTracker.Core.Interfaces.UpdateInfo?> CheckForUpdatesAsync()
    {
        try
        {
            // Initialize SparkleUpdater with the appcast URL
            using var sparkle = new SparkleUpdater(APPCAST_URL, new Ed25519Checker(SecurityMode.Unsafe));
            
            _logger.LogDebug("Checking for updates via NetSparkle appcast: {Url}", APPCAST_URL);
            
            // Check for updates quietly (no UI)
            var updateInfo = await sparkle.CheckForUpdatesQuietly();
            
            if (updateInfo?.Updates?.Any() == true)
            {
                var latest = updateInfo.Updates.First();
                var currentVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName()?.Version ?? new Version(1, 0, 0);
                
                // Parse version (handle 'v' prefix)
                var latestVersionStr = latest.Version?.TrimStart('v') ?? "0.0.0";
                
                if (Version.TryParse(latestVersionStr, out var latestVersion))
                {
                    if (latestVersion > currentVersion)
                    {
                        _logger.LogInformation("New version available: {LatestVersion} (Current: {CurrentVersion})", 
                            latestVersion, currentVersion);

                        return new AIConsumptionTracker.Core.Interfaces.UpdateInfo
                        {
                            Version = latest.Version ?? latestVersion.ToString(),
                            ReleaseUrl = latest.ReleaseNotesLink ?? $"https://github.com/rygel/AIConsumptionTracker/releases/tag/v{latestVersion}",
                            DownloadUrl = latest.DownloadLink ?? string.Empty,
                            ReleaseNotes = string.Empty,
                            PublishedAt = latest.PublicationDate
                        };
                    }
                }
            }
            
            _logger.LogDebug("No updates available or already on latest version");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check for updates via NetSparkle appcast");
            return null;
        }
    }

    public async Task<bool> DownloadAndInstallUpdateAsync(AIConsumptionTracker.Core.Interfaces.UpdateInfo updateInfo, IProgress<double>? progress = null)
    {
        try
        {
            _logger.LogInformation("Starting download and install for version {Version}", updateInfo.Version);

            if (string.IsNullOrEmpty(updateInfo.DownloadUrl))
            {
                _logger.LogWarning("No download URL available for update");
                return false;
            }

            // Create temp directory for download
            var tempDir = Path.Combine(Path.GetTempPath(), "AIConsumptionTracker_Updates");
            Directory.CreateDirectory(tempDir);
            var downloadPath = Path.Combine(tempDir, $"AIConsumptionTracker_Setup_{updateInfo.Version}.exe");

            // Download the file
            _logger.LogInformation("Downloading from {Url} to {Path}", updateInfo.DownloadUrl, downloadPath);
            using (var client = new System.Net.Http.HttpClient())
            {
                var response = await client.GetAsync(updateInfo.DownloadUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var downloadedBytes = 0L;
                var buffer = new byte[8192];
                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    int read;
                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read);
                        downloadedBytes += read;
                        if (totalBytes > 0 && progress != null)
                        {
                            var percentage = (double)downloadedBytes / totalBytes * 100;
                            progress.Report(percentage);
                        }
                    }
                }
            }

            _logger.LogInformation("Download completed successfully to {Path}", downloadPath);

            // Verify file exists
            if (!File.Exists(downloadPath))
            {
                _logger.LogError("Downloaded file not found at {Path}", downloadPath);
                return false;
            }

            // Run the installer silently
            _logger.LogInformation("Starting installer...");
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = downloadPath,
                    Arguments = "/SILENT /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
                    UseShellExecute = true,
                    Verb = "runas" // Run as administrator
                }
            };

            if (process.Start())
            {
                _logger.LogInformation("Installer started successfully. Application will restart.");
                return true;
            }
            else
            {
                _logger.LogError("Failed to start installer");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during download and install");
            return false;
        }
    }
}
