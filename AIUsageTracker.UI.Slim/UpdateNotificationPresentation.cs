// <copyright file="UpdateNotificationPresentation.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal sealed record UpdateNotificationPresentation(
    bool ApplyBannerVisibility,
    bool ShowBanner,
    bool ApplyBannerText,
    string BannerText);
