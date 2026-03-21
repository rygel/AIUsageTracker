// <copyright file="WebProviderUsageMapper.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;

using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Web.Services;

internal static class WebProviderUsageMapper
{
    public static ProviderUsage Map(object row)
    {
        var dictionary = row as IDictionary<string, object>;
        var providerId = GetString(dictionary, row, "provider_id", "ProviderId") ?? string.Empty;

        var usage = new ProviderUsage
        {
            ProviderId = providerId,
            ProviderName = GetString(dictionary, row, "provider_name", "ProviderName") ?? providerId,
            IsAvailable = GetBoolean(dictionary, row, "is_available", "IsAvailable"),
            Description = GetString(dictionary, row, "status_message", "StatusMessage") ?? string.Empty,
            RequestsUsed = GetDouble(dictionary, row, "requests_used", "RequestsUsed"),
            RequestsAvailable = GetDouble(dictionary, row, "requests_available", "RequestsAvailable"),
            UsedPercent = GetDouble(dictionary, row, "requests_percentage", "UsedPercent"),
            ResponseLatencyMs = GetDouble(dictionary, row, "response_latency_ms", "ResponseLatencyMs"),
            FetchedAt = GetDateTime(dictionary, row, "fetched_at", "FetchedAt"),
        };

        var nextResetTime = GetNullableDateTime(dictionary, row, "next_reset_time", "NextResetTime");
        if (nextResetTime.HasValue)
        {
            usage.NextResetTime = nextResetTime.Value;
        }

        return usage;
    }

    private static object? GetValue(IDictionary<string, object>? dictionary, object row, params string[] names)
    {
        if (dictionary is not null)
        {
            foreach (var name in names)
            {
                if (dictionary.TryGetValue(name, out var value))
                {
                    return value;
                }
            }
        }

        var rowType = row.GetType();
        foreach (var name in names)
        {
            var property = rowType.GetProperty(name);
            if (property is not null)
            {
                return property.GetValue(row);
            }
        }

        return null;
    }

    private static string? GetString(IDictionary<string, object>? dictionary, object row, params string[] names)
    {
        var value = GetValue(dictionary, row, names);
        if (value is null || value is DBNull)
        {
            return null;
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private static bool GetBoolean(IDictionary<string, object>? dictionary, object row, params string[] names)
    {
        var value = GetValue(dictionary, row, names);
        if (value is null || value is DBNull)
        {
            return false;
        }

        if (value is bool boolValue)
        {
            return boolValue;
        }

        if (value is byte byteValue)
        {
            return byteValue != 0;
        }

        if (value is short shortValue)
        {
            return shortValue != 0;
        }

        if (value is int intValue)
        {
            return intValue != 0;
        }

        if (value is long longValue)
        {
            return longValue != 0;
        }

        if (value is string text)
        {
            if (bool.TryParse(text, out var parsedBool))
            {
                return parsedBool;
            }

            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedNumber))
            {
                return parsedNumber != 0;
            }
        }

        return false;
    }

    private static double GetDouble(IDictionary<string, object>? dictionary, object row, params string[] names)
    {
        var value = GetValue(dictionary, row, names);
        if (value is null || value is DBNull)
        {
            return 0;
        }

        if (value is double doubleValue)
        {
            return doubleValue;
        }

        return Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }

    private static DateTime GetDateTime(IDictionary<string, object>? dictionary, object row, params string[] names)
    {
        return GetNullableDateTime(dictionary, row, names) ?? DateTime.UnixEpoch;
    }

    private static DateTime? GetNullableDateTime(IDictionary<string, object>? dictionary, object row, params string[] names)
    {
        var value = GetValue(dictionary, row, names);
        if (value is null || value is DBNull)
        {
            return null;
        }

        if (value is DateTime dateTime)
        {
            return dateTime;
        }

        if (value is DateTimeOffset dateTimeOffset)
        {
            return dateTimeOffset.UtcDateTime;
        }

        if (value is int intEpoch)
        {
            return DateTimeOffset.FromUnixTimeSeconds(intEpoch).UtcDateTime;
        }

        if (value is long longEpoch)
        {
            return DateTimeOffset.FromUnixTimeSeconds(longEpoch).UtcDateTime;
        }

        if (value is string text)
        {
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedEpoch))
            {
                return DateTimeOffset.FromUnixTimeSeconds(parsedEpoch).UtcDateTime;
            }

            if (DateTime.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsedDateTime))
            {
                return parsedDateTime;
            }
        }

        return null;
    }
}
