using Microsoft.Extensions.Logging;
using NetSparkleUpdater;
using NetSparkleUpdater.AppCastHandlers;
using AIConsumptionTracker.Core.Interfaces;

namespace AIConsumptionTracker.Infrastructure.Services;

public class GitHubUpdateChecker : IUpdateCheckerService
{
    private readonly ILogger<GitHubUpdateChecker> _logger;
    private const string REPO_OWNER = "rygel";
    private const string REPO_NAME = "AIConsumptionTracker";

    public GitHubUpdateChecker(ILogger<GitHubUpdateChecker> logger)
    {
        _logger = logger;
    }

    public async Task<AIConsumptionTracker.Core.Interfaces.UpdateInfo?> CheckForUpdatesAsync()
    {
        try
        {
            var appCastHandler = new GitHubReleaseAppCast(
                $"https://github.com/{REPO_OWNER}/{REPO_NAME}/releases",
                false); // false = use releases, not body

            var items = await appCastHandler.GetAppCastItems();
            if (items == null || items.Count == 0) return null;

            // Get the latest item (first one usually)
            var latest = items[0];
            
            var currentVersion = System.Reflection.Assembly.GetEntryAssembly()?.GetName()?.Version ?? new Version(1, 0, 0);
            
            var latestVersionStr = latest.Version.StartsWith("v") ? latest.Version[1..] : latest.Version;
            
            if (Version.TryParse(latestVersionStr, out var latestVersion))
            {
                if (latestVersion > currentVersion)
                {
                    _logger.LogInformation($"New version found: {latestVersion} (Current: {currentVersion})");

                    return new AIConsumptionTracker.Core.Interfaces.UpdateInfo
                    {
                        Version = latest.Version,
                        ReleaseUrl = latest.ReleaseNotesLink,
                        DownloadUrl = latest.DownloadLink,
                        ReleaseNotes = string.Empty, // AppCast items don't have full body easily
                        PublishedAt = latest.PublicationDate
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check for updates via NetSparkle GitHub handler");
        }
        
        return null;
    }

}
