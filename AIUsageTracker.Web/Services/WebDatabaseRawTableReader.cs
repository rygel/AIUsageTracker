// <copyright file="WebDatabaseRawTableReader.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

namespace AIUsageTracker.Web.Services
{
    using Dapper;
    using Microsoft.Data.Sqlite;

    internal static class WebDatabaseRawTableReader
    {
        public static async Task<(IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows, int TotalCount)> ReadTableAsync(
            SqliteConnection connection,
            string tableName,
            int page,
            int pageSize,
            string? orderBy = null)
        {
            var offset = (page - 1) * pageSize;
            var orderClause = string.IsNullOrEmpty(orderBy) ? string.Empty : $"ORDER BY {orderBy}";

            var totalCount = await connection.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {tableName}").ConfigureAwait(false);
            var sql = $"SELECT * FROM {tableName} {orderClause} LIMIT {pageSize} OFFSET {offset}";
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
}
