using NetSparkleUpdater;
using AIUsageTracker.Core.Interfaces;

namespace AIUsageTracker.Infrastructure.Mappers;

public static class UpdateMapper
{
    public static AppCastItem ToAppCastItem(AIUsageTracker.Core.Interfaces.UpdateInfo info)
    {
        return new AppCastItem
        {
            Version = info.Version.StartsWith("v") ? info.Version[1..] : info.Version,
            DownloadLink = info.DownloadUrl,
            ReleaseNotesLink = info.ReleaseUrl,
            PublicationDate = info.PublishedAt,
            IsCriticalUpdate = false
        };
    }
}

