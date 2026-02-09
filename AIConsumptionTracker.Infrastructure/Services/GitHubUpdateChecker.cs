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
}
