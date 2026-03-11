// <copyright file="DatabaseMigrationServiceTests.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using AIUsageTracker.Monitor.Services;
using AIUsageTracker.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Moq;

namespace AIUsageTracker.Monitor.Tests;

public class DatabaseMigrationServiceTests
{
    [Fact]
    public void RunMigrations_LegacyDatabaseWithoutChangelog_RemovesLegacyAnthropicRows()
    {
        var root = TestTempPaths.CreateDirectory("migration-anthropic-cleanup");
        var dbPath = Path.Combine(root, "usage.db");

        try
        {
            SeedLegacySchemaWithAnthropicRows(dbPath);

            var logger = new Mock<ILogger<DatabaseMigrationService>>();
            var service = new DatabaseMigrationService(dbPath, logger.Object);

            service.RunMigrations();

            using var connection = new SqliteConnection($"Data Source={dbPath}");
            connection.Open();

            Assert.Equal(0, ExecuteScalarInt(connection, "SELECT COUNT(*) FROM providers WHERE lower(provider_id) = 'anthropic';"));
            Assert.Equal(0, ExecuteScalarInt(connection, "SELECT COUNT(*) FROM provider_history WHERE lower(provider_id) = 'anthropic';"));
            Assert.Equal(0, ExecuteScalarInt(connection, "SELECT COUNT(*) FROM raw_snapshots WHERE lower(provider_id) = 'anthropic';"));
            Assert.Equal(0, ExecuteScalarInt(connection, "SELECT COUNT(*) FROM reset_events WHERE lower(provider_id) = 'anthropic';"));

            Assert.Equal(1, ExecuteScalarInt(connection, "SELECT COUNT(*) FROM providers WHERE lower(provider_id) = 'openai';"));
            Assert.Equal(1, ExecuteScalarInt(connection, "SELECT COUNT(*) FROM provider_history WHERE lower(provider_id) = 'openai';"));
        }
        finally
        {
            TestTempPaths.CleanupPath(root);
        }
    }

    private static void SeedLegacySchemaWithAnthropicRows(string dbPath)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath}");
        connection.Open();

        ExecuteNonQuery(connection, """
            CREATE TABLE providers (
                provider_id TEXT PRIMARY KEY,
                provider_name TEXT,
                created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                is_active INTEGER NOT NULL DEFAULT 1,
                config_json TEXT,
                auth_source TEXT DEFAULT 'manual'
            );

            CREATE TABLE provider_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                provider_id TEXT NOT NULL,
                is_available INTEGER NOT NULL DEFAULT 1,
                status_message TEXT NOT NULL DEFAULT '',
                next_reset_time TEXT,
                requests_used REAL NOT NULL DEFAULT 0,
                requests_available REAL NOT NULL DEFAULT 0,
                requests_percentage REAL NOT NULL DEFAULT 0,
                response_latency_ms REAL NOT NULL DEFAULT 0,
                fetched_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
                details_json TEXT
            );

            CREATE TABLE raw_snapshots (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                provider_id TEXT NOT NULL,
                raw_json TEXT NOT NULL,
                http_status INTEGER NOT NULL DEFAULT 200,
                fetched_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );

            CREATE TABLE reset_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                provider_id TEXT NOT NULL,
                provider_name TEXT NOT NULL,
                previous_usage REAL,
                new_usage REAL,
                reset_type TEXT NOT NULL,
                timestamp TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
            );
            """);

        ExecuteNonQuery(connection, """
            INSERT INTO providers (provider_id, provider_name) VALUES ('anthropic', 'Anthropic');
            INSERT INTO providers (provider_id, provider_name) VALUES ('openai', 'OpenAI');

            INSERT INTO provider_history (provider_id, status_message) VALUES ('anthropic', 'legacy');
            INSERT INTO provider_history (provider_id, status_message) VALUES ('openai', 'active');

            INSERT INTO raw_snapshots (provider_id, raw_json, http_status) VALUES ('anthropic', '{}', 200);
            INSERT INTO reset_events (provider_id, provider_name, reset_type) VALUES ('anthropic', 'Anthropic', 'manual');
            """);
    }

    private static void ExecuteNonQuery(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static int ExecuteScalarInt(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
