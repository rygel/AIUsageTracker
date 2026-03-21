// <copyright file="UpdateNotificationPresentationCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal static class UpdateNotificationPresentationCatalog
{
    public static UpdateNotificationPresentation Create(
        string? latestVersion,
        bool hasBanner,
        bool hasText)
    {
        if (!string.IsNullOrWhiteSpace(latestVersion))
        {
            if (hasBanner && hasText)
            {
                return new UpdateNotificationPresentation(
                    ApplyBannerVisibility: true,
                    ShowBanner: true,
                    ApplyBannerText: true,
                    BannerText: $"New version available: {latestVersion}");
            }

            return new UpdateNotificationPresentation(
                ApplyBannerVisibility: false,
                ShowBanner: false,
                ApplyBannerText: false,
                BannerText: string.Empty);
        }

        if (hasBanner)
        {
            return new UpdateNotificationPresentation(
                ApplyBannerVisibility: true,
                ShowBanner: false,
                ApplyBannerText: false,
                BannerText: string.Empty);
        }

        return new UpdateNotificationPresentation(
            ApplyBannerVisibility: false,
            ShowBanner: false,
            ApplyBannerText: false,
            BannerText: string.Empty);
    }
}
