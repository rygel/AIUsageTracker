// <copyright file="FontSelectionHelper.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Infrastructure.Helpers;

public static class FontSelectionHelper
{
    public static string GetSelectedFont(string? currentPreference, IEnumerable<string> availableFonts)
    {
        if (string.IsNullOrWhiteSpace(currentPreference))
        {
            return availableFonts.FirstOrDefault(f => f.Equals("Segoe UI", StringComparison.OrdinalIgnoreCase))
                   ?? availableFonts.FirstOrDefault()
                   ?? string.Empty;
        }

        var match = availableFonts.FirstOrDefault(f => f.Equals(currentPreference, StringComparison.OrdinalIgnoreCase));
        return match ?? currentPreference;
    }
}
