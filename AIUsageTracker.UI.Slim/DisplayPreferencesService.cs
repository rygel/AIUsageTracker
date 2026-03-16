// <copyright file="DisplayPreferencesService.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.UI.Slim;

public sealed class DisplayPreferencesService
{
    public PercentageDisplayMode GetPercentageDisplayMode(AppPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        return preferences.PercentageDisplayMode;
    }

    public bool ShouldShowUsedPercentages(AppPreferences preferences)
    {
        return this.GetPercentageDisplayMode(preferences) == PercentageDisplayMode.Used;
    }

    public void SetShowUsedPercentages(AppPreferences preferences, bool showUsed)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        preferences.ShowUsedPercentages = showUsed;
        preferences.SchemaVersion = AppPreferences.CurrentSchemaVersion;
    }
}
