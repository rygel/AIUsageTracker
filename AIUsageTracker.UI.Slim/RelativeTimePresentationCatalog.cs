// <copyright file="RelativeTimePresentationCatalog.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.UI.Slim;

internal static class RelativeTimePresentationCatalog
{
    public static string FormatUntil(DateTime nextReset, DateTime now)
    {
        var diff = nextReset - now;

        if (diff.TotalSeconds <= 0)
        {
            return "0m";
        }

        if (diff.TotalDays >= 1)
        {
            return $"{diff.Days}d {diff.Hours}h";
        }

        if (diff.TotalHours >= 1)
        {
            return $"{diff.Hours}h {diff.Minutes}m";
        }

        return $"{Math.Max(1, (int)Math.Ceiling(diff.TotalMinutes))}m";
    }
}
