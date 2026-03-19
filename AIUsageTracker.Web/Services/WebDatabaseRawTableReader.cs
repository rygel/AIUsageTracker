// <copyright file="WebDatabaseRawTableReader.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using Dapper;
using Microsoft.Data.Sqlite;

namespace AIUsageTracker.Web.Services;

internal static class WebDatabaseRawTableReader
{
    private static readonly IReadOnlySet<string> AllowedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "providers", "provider_history", "raw_snapshots", "reset_events",
    };

    // Allowed order-by clauses expressed as exact strings (column name + optional direction).
    // Column identifiers cannot be parameterized in SQLite, so we whitelist explicitly.
    private static readonly IReadOnlySet<string> AllowedOrderByClauses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "fetched_at DESC", "fetched_at ASC", "timestamp DESC", "timestamp ASC", "id DESC", "id ASC",
    };

    public static async Task<(IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows, int TotalCount)> ReadTableAsync(
        SqliteConnection connection,
        string tableName,
        int page,
        int pageSize,
        string? orderBy = null)
    {
        if (!AllowedTables.Contains(tableName))
        {
            throw new ArgumentException($"Table '{tableName}' is not in the allowed list.", nameof(tableName));
        }

        if (!string.IsNullOrEmpty(orderBy) && !AllowedOrderByClauses.Contains(orderBy))
        {
            throw new ArgumentException($"Order-by clause '{orderBy}' is not in the allowed list.", nameof(orderBy));
        }

        var offset = (page - 1) * pageSize;
        var orderClause = string.IsNullOrEmpty(orderBy) ? string.Empty : $"ORDER BY {orderBy}"; // sql-interpolation-allow — orderBy is from AllowedOrderByClauses whitelist above

        var totalCount = await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {tableName}").ConfigureAwait(false); // sql-interpolation-allow — tableName is from AllowedTables whitelist above
        var sql = $"SELECT * FROM {tableName} {orderClause} LIMIT {pageSize} OFFSET {offset}"; // sql-interpolation-allow — tableName/orderClause are whitelist-validated; pageSize/offset are integers
        var rows = new List<Dictionary<string, object?>>();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            var row = new Dictionary<string, object?>(StringComparer.Ordinal);
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var value = await reader.IsDBNullAsync(i).ConfigureAwait(false) ? null : reader.GetValue(i);
                row[reader.GetName(i)] = value;
            }

            rows.Add(row);
        }

        return (rows, totalCount);
    }
}
