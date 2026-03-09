// <copyright file="UpdateMapper.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Infrastructure.Mappers
{
    using NetSparkleUpdater;
    using AIUsageTracker.Core.Interfaces;

    public static class UpdateMapper
    {
        public static AppCastItem ToAppCastItem(AIUsageTracker.Core.Interfaces.UpdateInfo info)
        {
            return new AppCastItem
            {
                Version = info.Version.StartsWith("v", StringComparison.Ordinal) ? info.Version[1..] : info.Version,
                DownloadLink = info.DownloadUrl,
                ReleaseNotesLink = info.ReleaseUrl,
                PublicationDate = info.PublishedAt,
                IsCriticalUpdate = false
            };
        }
    }

}
