// <copyright file="UpdateMapper.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using NetSparkleUpdater;
using CoreUpdateInfo = AIUsageTracker.Core.Interfaces.UpdateInfo;

namespace AIUsageTracker.Infrastructure.Mappers;

public static class UpdateMapper
{
    public static AppCastItem ToAppCastItem(CoreUpdateInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);

        return new AppCastItem
        {
            Version = info.Version.StartsWith("v", StringComparison.Ordinal) ? info.Version[1..] : info.Version,
            DownloadLink = info.DownloadUrl,
            ReleaseNotesLink = info.ReleaseUrl,
            PublicationDate = info.PublishedAt,
            IsCriticalUpdate = false,
        };
    }
}
