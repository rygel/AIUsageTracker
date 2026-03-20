// <copyright file="WebProviderUsageMapper.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Globalization;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Web.Services;

internal static class WebProviderUsageMapper
{
    public static ProviderUsage Map(dynamic row)
    {
        var usage = new ProviderUsage
        {
            ProviderId = row.provider_id ?? row.ProviderId,
            ProviderName = row.ProviderName,
            IsAvailable = row.is_available == 1 || (row.IsAvailable != null && row.IsAvailable == 1),
            Description = row.status_message ?? string.Empty,
            RequestsUsed = (double)(row.requests_used ?? row.RequestsUsed ?? 0.0),
            RequestsAvailable = (double)(row.requests_available ?? row.RequestsAvailable ?? 0.0),
            UsedPercent = (double)(row.requests_percentage ?? row.UsedPercent ?? 0.0),
            ResponseLatencyMs = (double)(row.response_latency_ms ?? row.ResponseLatencyMs ?? 0.0),
            FetchedAt = ParseDateTimeUtc(row.fetched_at ?? row.FetchedAt),
        };

        usage.NextResetTime = ParseNullableDateTimeUtc(row.next_reset_time ?? row.NextResetTime);

        return usage;
    }

    private static DateTime? ParseNullableDateTimeUtc(object? value)
    {
        if (value == null || value is DBNull)
        {
            return null;
        }

        if (value is string text && string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return ParseDateTimeUtc(value);
    }

    private static DateTime ParseDateTimeUtc(object? value)
    {
        if (value == null || value is DBNull)
        {
            return DateTime.UtcNow;
        }

        if (value is DateTime dateTime)
        {
            return dateTime.Kind == DateTimeKind.Utc ? dateTime : dateTime.ToUniversalTime();
        }

        if (value is DateTimeOffset dateTimeOffset)
        {
            return dateTimeOffset.UtcDateTime;
        }

        if (TryParseEpochSeconds(value, out var epochSeconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(epochSeconds).UtcDateTime;
        }

        if (DateTime.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }

        return DateTime.Parse(
            Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    private static bool TryParseEpochSeconds(object value, out long epochSeconds)
    {
        switch (value)
        {
            case long longValue:
                epochSeconds = longValue;
                return true;
            case int intValue:
                epochSeconds = intValue;
                return true;
            case short shortValue:
                epochSeconds = shortValue;
                return true;
            case double doubleValue when double.IsFinite(doubleValue):
                epochSeconds = Convert.ToInt64(doubleValue, CultureInfo.InvariantCulture);
                return true;
            case decimal decimalValue:
                epochSeconds = Convert.ToInt64(decimalValue, CultureInfo.InvariantCulture);
                return true;
            case string stringValue when long.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                epochSeconds = parsed;
                return true;
            default:
                epochSeconds = 0;
                return false;
        }
    }
}
