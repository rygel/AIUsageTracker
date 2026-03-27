// <copyright file="ResetTimeParser.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System;
using System.Globalization;
using System.Text.Json;

namespace AIUsageTracker.Core.Utilities;

/// <summary>
/// Utility class for parsing reset times from various formats used by different providers.
/// </summary>
public static class ResetTimeParser
{
    // Unix timestamp (seconds) for year 2100 — used to distinguish seconds from milliseconds.
    private const long Year2100UnixSeconds = 4_102_444_800L;

    /// <summary>
    /// Parses a reset time from a Unix timestamp in seconds.
    /// </summary>
    /// <param name="unixSeconds">Unix timestamp in seconds since epoch.</param>
    /// <returns>Local DateTime if valid, null otherwise.</returns>
    public static DateTime? FromUnixSeconds(long? unixSeconds)
    {
        if (!unixSeconds.HasValue || unixSeconds.Value <= 0)
        {
            return null;
        }

        try
        {
            var utcTime = DateTimeOffset.FromUnixTimeSeconds(unixSeconds.Value);
            return utcTime.LocalDateTime;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a reset time from a Unix timestamp in milliseconds.
    /// </summary>
    /// <param name="unixMilliseconds">Unix timestamp in milliseconds since epoch.</param>
    /// <returns>Local DateTime if valid, null otherwise.</returns>
    public static DateTime? FromUnixMilliseconds(long? unixMilliseconds)
    {
        if (!unixMilliseconds.HasValue || unixMilliseconds.Value <= 0)
        {
            return null;
        }

        try
        {
            var utcTime = DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds.Value);
            return utcTime.LocalDateTime;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a reset time from seconds relative to now (e.g., "resets in X seconds").
    /// </summary>
    /// <param name="secondsFromNow">Number of seconds from now.</param>
    /// <returns>Local DateTime if valid, null otherwise.</returns>
    public static DateTime? FromSecondsFromNow(double? secondsFromNow)
    {
        if (!secondsFromNow.HasValue || secondsFromNow.Value <= 0)
        {
            return null;
        }

        try
        {
            return DateTime.UtcNow.Add(TimeSpan.FromSeconds(secondsFromNow.Value));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses a reset time from an ISO 8601 string.
    /// </summary>
    /// <param name="isoString">ISO 8601 formatted date string.</param>
    /// <returns>Local DateTime if valid, null otherwise.</returns>
    public static DateTime? FromIso8601(string? isoString)
    {
        if (string.IsNullOrWhiteSpace(isoString))
        {
            return null;
        }

        if (DateTime.TryParse(isoString, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt))
        {
            return dt.Kind == DateTimeKind.Utc ? dt.ToLocalTime() : dt;
        }

        return null;
    }

    /// <summary>
    /// Parses a reset time from a string using multiple common formats.
    /// Tries ISO 8601 first, then falls back to general parsing.
    /// </summary>
    /// <param name="dateString">Date string in various formats.</param>
    /// <returns>Local DateTime if valid, null otherwise.</returns>
    public static DateTime? Parse(string? dateString)
    {
        if (string.IsNullOrWhiteSpace(dateString))
        {
            return null;
        }

        // Try ISO 8601 first (most common)
        var isoResult = FromIso8601(dateString);
        if (isoResult.HasValue)
        {
            return isoResult;
        }

        // Try common formats
        var formats = new[]
        {
            "yyyy-MM-ddTHH:mm:ss.fffZ",
            "yyyy-MM-ddTHH:mm:ssZ",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd",
            "MM/dd/yyyy HH:mm:ss",
            "MM/dd/yyyy",
            "dd/MM/yyyy HH:mm:ss",
            "dd/MM/yyyy",
        };

        if (DateTime.TryParseExact(dateString, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
        {
            return dt.ToLocalTime();
        }

        // Last resort: try general parsing
        if (DateTime.TryParse(dateString, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
        {
            return dt.Kind == DateTimeKind.Utc ? dt.ToLocalTime() : dt;
        }

        return null;
    }

    /// <summary>
    /// Parses a reset time from a JsonElement that may contain various formats.
    /// </summary>
    /// <param name="element">JsonElement containing the reset time.</param>
    /// <returns>Local DateTime if valid, null otherwise.</returns>
    public static DateTime? FromJsonElement(JsonElement element)
    {
        try
        {
            // Try as Unix timestamp (number)
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var longValue))
            {
                // Assume seconds if value is reasonably small, otherwise assume milliseconds
                if (longValue < Year2100UnixSeconds)
                {
                    return FromUnixSeconds(longValue);
                }

                return FromUnixMilliseconds(longValue);
            }

            // Try as string
            if (element.ValueKind == JsonValueKind.String)
            {
                var stringValue = element.GetString();
                return Parse(stringValue);
            }
        }
        catch
        {
            // Ignore parsing errors
        }

        return null;
    }

    /// <summary>
    /// Formats a reset time for display in a consistent format.
    /// </summary>
    /// <param name="resetTime">The reset time.</param>
    /// <param name="format">Optional custom format string.</param>
    /// <returns>Formatted string or empty string if null.</returns>
    public static string FormatForDisplay(DateTime? resetTime, string? format = null)
    {
        if (!resetTime.HasValue)
        {
            return string.Empty;
        }

        format ??= "MMM dd HH:mm";
        return $"(Resets: ({resetTime.Value.ToString(format, CultureInfo.InvariantCulture)}))";
    }

    /// <summary>
    /// Gets the soonest reset time from a collection of reset times.
    /// </summary>
    /// <param name="resetTimes">Collection of reset times (may contain nulls).</param>
    /// <returns>The soonest valid reset time, or null if none found.</returns>
    public static DateTime? GetSoonest(params DateTime?[] resetTimes)
    {
        DateTime? soonest = null;

        foreach (var rt in resetTimes)
        {
            if (!rt.HasValue)
            {
                continue;
            }

            if (!soonest.HasValue || rt.Value < soonest.Value)
            {
                soonest = rt.Value;
            }
        }

        return soonest;
    }

    /// <summary>
    /// Checks if a reset time is in the future (has not occurred yet).
    /// </summary>
    /// <param name="resetTime">The reset time to check.</param>
    /// <param name="now">Optional reference time (defaults to DateTime.UtcNow).</param>
    /// <returns>True if the reset time is in the future, false otherwise.</returns>
    public static bool IsFuture(DateTime? resetTime, DateTime? now = null)
    {
        if (!resetTime.HasValue)
        {
            return true; // No reset time means always active/no expiration
        }

        var referenceTime = now ?? DateTime.UtcNow;
        return resetTime.Value > referenceTime;
    }

    /// <summary>
    /// Calculates the time remaining until a reset.
    /// </summary>
    /// <param name="resetTime">The reset time.</param>
    /// <param name="now">Optional reference time (defaults to DateTime.UtcNow).</param>
    /// <returns>TimeSpan representing time remaining, or TimeSpan.Zero if reset time is null or in the past.</returns>
    public static TimeSpan GetTimeRemaining(DateTime? resetTime, DateTime? now = null)
    {
        if (!resetTime.HasValue)
        {
            return TimeSpan.Zero;
        }

        var referenceTime = now ?? DateTime.UtcNow;

        if (resetTime.Value <= referenceTime)
        {
            return TimeSpan.Zero;
        }

        return resetTime.Value - referenceTime;
    }
}
