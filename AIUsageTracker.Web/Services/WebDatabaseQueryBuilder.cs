// <copyright file="WebDatabaseQueryBuilder.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Web.Services;

internal static class WebDatabaseQueryBuilder
{
    public static string BuildLatestUsageQuery(bool includeInactive)
    {
        var sql = @"
            SELECT h.*, p.provider_name as ProviderName 
            FROM provider_history h
            JOIN providers p ON h.provider_id = p.provider_id
            WHERE h.id IN (SELECT MAX(id) FROM provider_history GROUP BY provider_id)";

        if (!includeInactive)
        {
            sql += " AND p.is_active = 1 AND h.is_available = 1";
        }

        return sql;
    }

    public static string BuildHistoryQuery(int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 10_000);
        return $@"
            SELECT h.*, p.provider_name as ProviderName
            FROM provider_history h
            JOIN providers p ON h.provider_id = p.provider_id
            ORDER BY h.fetched_at DESC
            LIMIT {limit}"; // sql-interpolation-allow — limit is a bounds-validated integer (ThrowIfNegativeOrZero / ThrowIfGreaterThan)
    }

    public static string BuildProviderHistoryQuery(int limit)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 10_000);
        return $@"
            SELECT h.*, p.provider_name as ProviderName
            FROM provider_history h
            JOIN providers p ON h.provider_id = p.provider_id
            WHERE h.provider_id = @ProviderId
            ORDER BY h.fetched_at DESC
            LIMIT {limit}"; // sql-interpolation-allow — limit is a bounds-validated integer (ThrowIfNegativeOrZero / ThrowIfGreaterThan)
    }

    public static string BuildExportHistoryQuery(int limit)
    {
        if (limit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be 0 (no limit) or a positive value.");
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 100_000);
        var sql = @"
            SELECT h.*, p.provider_name as ProviderName
            FROM provider_history h
            JOIN providers p ON h.provider_id = p.provider_id
            ORDER BY h.fetched_at DESC";

        if (limit > 0)
        {
            sql += $" LIMIT {limit}"; // sql-interpolation-allow — limit is a bounds-validated integer (ThrowIfGreaterThan)
        }

        return sql;
    }
}
