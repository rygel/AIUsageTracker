using NetSparkleUpdater;
using AIConsumptionTracker.Core.Interfaces;

namespace AIConsumptionTracker.Infrastructure.Mappers;

public static class UpdateMapper
{
    public static AppCastItem ToAppCastItem(AIConsumptionTracker.Core.Interfaces.UpdateInfo info)
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
