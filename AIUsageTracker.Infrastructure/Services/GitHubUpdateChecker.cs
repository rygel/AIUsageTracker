using Microsoft.Extensions.Logging;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.SignatureVerifiers;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Infrastructure.Services;

public class GitHubUpdateChecker : IUpdateCheckerService
{
    private readonly ILogger<GitHubUpdateChecker> _logger;
    private readonly UpdateChannel _channel;
    
    // Architecture-specific appcast URLs
    private static readonly Dictionary<string, string> STABLE_APPCAST_URLS = new()
    {
        ["x64"] = "https://github.com/rygel/AIConsumptionTracker/releases/latest/download/appcast_x64.xml",
        ["x86"] = "https://github.com/rygel/AIConsumptionTracker/releases/latest/download/appcast_x86.xml",
        ["arm64"] = "https://github.com/rygel/AIConsumptionTracker/releases/latest/download/appcast_arm64.xml"
    };
    
    private static readonly Dictionary<string, string> BETA_APPCAST_URLS = new()
    {
        ["x64"] = "https://github.com/rygel/AIConsumptionTracker/releases/latest/download/appcast_beta_x64.xml",
        ["x86"] = "https://github.com/rygel/AIConsumptionTracker/releases/latest/download/appcast_beta_x86.xml",
        ["arm64"] = "https://github.com/rygel/AIConsumptionTracker/releases/latest/download/appcast_beta_arm64.xml"
    };

    private string GetAppcastUrlForCurrentArchitecture()
    {
        var currentArch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLower();
        
        // Map architecture names
        var archMapping = new Dictionary<string, string>
        {
            ["x64"] = "x64",
            ["x86"] = "x86",
            ["arm64"] = "arm64",
            ["arm"] = "arm64"
        };
        
        var targetArch = archMapping.GetValueOrDefault(currentArch, "x64");
        
        // Select URL based on channel
        var appcastUrls = _channel == UpdateChannel.Beta ? BETA_APPCAST_URLS : STABLE_APPCAST_URLS;
        var channelSuffix = _channel.ToAppcastSuffix();
        
        if (appcastUrls.TryGetValue(targetArch, out var url))
        {
            _logger.LogDebug("Using appcast for architecture {Architecture} ({Channel}): {Url}", targetArch, _channel, url);
            return url;
        }
        
        // Fallback to x64 if unknown
        _logger.LogWarning("Unknown architecture {Architecture}, falling back to x64", currentArch);
        return appcastUrls["x64"];
    }

    public GitHubUpdateChecker(ILogger<GitHubUpdateChecker> logger, UpdateChannel channel = UpdateChannel.Stable)
    {
        _logger = logger;
        _channel = channel;
    }

    public async Task<AIUsageTracker.Core.Interfaces.UpdateInfo?> CheckForUpdatesAsync()
    {
        try
        {
            // Get the appcast URL for current architecture
            var appcastUrl = GetAppcastUrlForCurrentArchitecture();
            
            // Initialize SparkleUpdater with the architecture-specific appcast URL
            using var sparkle = new SparkleUpdater(appcastUrl, new Ed25519Checker(SecurityMode.Unsafe));
            
            _logger.LogDebug("Checking for updates via NetSparkle appcast: {Url}", appcastUrl);
            
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

                        // Fetch release notes from GitHub API
                        var releaseNotes = await FetchReleaseNotesFromGitHub(latestVersionStr);

                        return new AIUsageTracker.Core.Interfaces.UpdateInfo
                        {
                            Version = latest.Version ?? latestVersion.ToString(),
                            ReleaseUrl = latest.ReleaseNotesLink ?? $"https://github.com/rygel/AIConsumptionTracker/releases/tag/v{latestVersion}",
                            DownloadUrl = latest.DownloadLink ?? string.Empty,
                            ReleaseNotes = releaseNotes,
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

    public async Task<bool> DownloadAndInstallUpdateAsync(AIUsageTracker.Core.Interfaces.UpdateInfo updateInfo, IProgress<double>? progress = null)
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
            var tempDir = Path.Combine(Path.GetTempPath(), "AIUsageTracker_Updates");
            Directory.CreateDirectory(tempDir);
            var downloadPath = Path.Combine(tempDir, $"AIUsageTracker_Setup_{updateInfo.Version}.exe");

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

            // Run the installer
            _logger.LogInformation("Starting installer...");
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = downloadPath,
                    Arguments = "/CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
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

    private async Task<string> FetchReleaseNotesFromGitHub(string version)
    {
        try
        {
            using var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "AIUsageTracker");
            
            var url = $"https://api.github.com/repos/rygel/AIConsumptionTracker/releases/tags/v{version}";
            _logger.LogDebug("Fetching release notes from: {Url}", url);
            
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch release notes: {StatusCode}", response.StatusCode);
                return string.Empty;
            }
            
            var content = await response.Content.ReadAsStringAsync();
            using var doc = System.Text.Json.JsonDocument.Parse(content);
            
            if (doc.RootElement.TryGetProperty("body", out var bodyElement))
            {
                var releaseNotes = bodyElement.GetString() ?? string.Empty;
                _logger.LogDebug("Successfully fetched release notes ({Length} chars)", releaseNotes.Length);
                return releaseNotes;
            }
            
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch release notes from GitHub API");
            return string.Empty;
        }
    }
}

