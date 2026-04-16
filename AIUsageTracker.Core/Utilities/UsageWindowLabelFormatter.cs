// <copyright file="UsageWindowLabelFormatter.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;

namespace AIUsageTracker.Core.Utilities;

/// <summary>
/// Provides a single formatting contract for provider usage window labels.
/// </summary>
public static class UsageWindowLabelFormatter
{
    /// <summary>
    /// Formats a duration/unit pair to a normalized compact label.
    /// </summary>
    /// <param name="duration">Duration value from provider payload.</param>
    /// <param name="unit">Provider-specific unit token (e.g. TIME_UNIT_MINUTE).</param>
    /// <returns>Normalized display label.</returns>
    public static string FormatDuration(long duration, string unit)
    {
        if (string.Equals(unit, "TIME_UNIT_MINUTE", StringComparison.Ordinal))
        {
            if (duration == 60)
            {
                return "Hourly";
            }

            if (duration > 60 && duration % 60 == 0)
            {
                return $"{(duration / 60).ToString(CultureInfo.InvariantCulture)}h";
            }

            return $"{duration.ToString(CultureInfo.InvariantCulture)}m";
        }

        if (string.Equals(unit, "TIME_UNIT_HOUR", StringComparison.Ordinal))
        {
            return duration == 1 ? "Hourly" : $"{duration.ToString(CultureInfo.InvariantCulture)}h";
        }

        if (string.Equals(unit, "TIME_UNIT_DAY", StringComparison.Ordinal))
        {
            return $"{duration.ToString(CultureInfo.InvariantCulture)}d";
        }

        return unit;
    }
}
