// <copyright file="ProviderSectionCollapseCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim;

internal static class ProviderSectionCollapseCatalog
{
    public static bool GetIsCollapsed(AppPreferences preferences, bool isQuotaBased)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        return isQuotaBased ? preferences.IsPlansAndQuotasCollapsed : preferences.IsPayAsYouGoCollapsed;
    }

    public static void SetIsCollapsed(AppPreferences preferences, bool isQuotaBased, bool isCollapsed)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (isQuotaBased)
        {
            preferences.IsPlansAndQuotasCollapsed = isCollapsed;
            return;
        }

        preferences.IsPayAsYouGoCollapsed = isCollapsed;
    }
}
