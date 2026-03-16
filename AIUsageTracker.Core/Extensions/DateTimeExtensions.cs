// <copyright file="DateTimeExtensions.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Core.Extensions;

public static class DateTimeExtensions
{
    public static DateTime UtcNow => DateTime.UtcNow;

    public static DateTime ToUniversalTime(this DateTime dateTime)
    {
        return dateTime.Kind == DateTimeKind.Utc ? dateTime : dateTime.ToUniversalTime();
    }

    public static DateTime ToLocalTime(this DateTime dateTime)
    {
        return dateTime.Kind == DateTimeKind.Local ? dateTime : dateTime.ToLocalTime();
    }

    public static DateTime ToUtcFromUnixSeconds(long unixSeconds)
    {
        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
    }

    public static DateTime ToUtcFromUnixMilliseconds(long unixMilliseconds)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds).UtcDateTime;
    }

    public static long ToUnixSeconds(this DateTime dateTime)
    {
        return new DateTimeOffset(dateTime).ToUnixTimeSeconds();
    }

    public static long ToUnixMilliseconds(this DateTime dateTime)
    {
        return new DateTimeOffset(dateTime).ToUnixTimeMilliseconds();
    }

    public static string ToIso8601(this DateTime dateTime)
    {
        return dateTime.ToUniversalTime().ToString("o");
    }

    public static bool IsRecent(this DateTime dateTime, TimeSpan threshold)
    {
        return UtcNow - dateTime.ToUniversalTime() < threshold;
    }

    public static TimeSpan TimeSince(this DateTime dateTime)
    {
        return UtcNow - dateTime.ToUniversalTime();
    }
}
