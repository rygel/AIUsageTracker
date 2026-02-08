using System.Reflection;
using Microsoft.Extensions.Logging;
using Octokit;
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

    public async Task<UpdateInfo?> CheckForUpdatesAsync()
    {
        try
        {
            var client = new GitHubClient(new ProductHeaderValue("AIConsumptionTracker"));
            var release = await client.Repository.Release.GetLatest(REPO_OWNER, REPO_NAME);

            var currentVersion = Assembly.GetEntryAssembly()?.GetName()?.Version ?? new Version(1, 0, 0);
            
            if (IsUpdateAvailable(currentVersion, release.TagName, out var latestVersion))
            {
                if (latestVersion! > currentVersion)
                {
                    _logger.LogInformation($"New version found: {latestVersion} (Current: {currentVersion})");
                    
                    var exeAsset = release.Assets.FirstOrDefault(a => a.Name.EndsWith(".exe"));
                    var downloadUrl = exeAsset?.BrowserDownloadUrl ?? release.HtmlUrl;

                    return new UpdateInfo
                    {
                        Version = release.TagName,
                        ReleaseUrl = release.HtmlUrl,
                        DownloadUrl = downloadUrl,
                        ReleaseNotes = release.Body,
                        PublishedAt = release.PublishedAt?.DateTime ?? DateTime.Now
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check for updates");
        }
        
        return null;
    }

    public static bool IsUpdateAvailable(Version current, string tagName, out Version? parsedLatest)
    {
        parsedLatest = null;
        if (string.IsNullOrWhiteSpace(tagName)) return false;

        // Tag usually "v1.7.3" -> "1.7.3"
        var tagVersionStr = tagName.StartsWith("v") ? tagName[1..] : tagName;

        if (Version.TryParse(tagVersionStr, out var latestVersion))
        {
            parsedLatest = latestVersion;
            // Strict check: latest > current
            return latestVersion > current;
        }
        
        return false;
    }
}
