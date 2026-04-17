// <copyright file="Program.cs" company="AIUsageTracker">
// Copyright (c) AIUsageTracker. All rights reserved.
// </copyright>

using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Seeder;

public class Program
{
    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length > 0 && string.Equals(args[0], "export", StringComparison.Ordinal))
        {
            var exportPath = ValidateFixturePath(args.Length > 1 ? args[1] : "test-fixtures/provider-data.json");
            if (exportPath == null)
            {
                return 1;
            }

            return ExportData(exportPath);
        }

        if (args.Length > 0 && string.Equals(args[0], "stats", StringComparison.Ordinal))
        {
            return PrintStats();
        }

        var seedPath = ValidateFixturePath(args.Length > 0 ? args[0] : "test-fixtures/provider-data.json");
        if (seedPath == null)
        {
            return 1;
        }

        return SeedDatabase(seedPath);
    }

    private static string? ValidateFixturePath(string relativePath)
    {
        var fullPath = Path.GetFullPath(relativePath);
        var workingDir = Path.GetFullPath(Environment.CurrentDirectory);

        if (!fullPath.StartsWith(workingDir, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Path traversal rejected: '{relativePath}' resolves outside the working directory.");
            return null;
        }

        return fullPath;
    }

    private static int PrintStats()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dbPath = Path.Combine(appData, "AIUsageTracker", "usage.db");

        if (!File.Exists(dbPath))
        {
            Console.Error.WriteLine($"Database not found: {dbPath}");
            return 1;
        }

        var fileSize = new FileInfo(dbPath).Length;
        Console.WriteLine($"DB file: {dbPath}");
        Console.WriteLine($"File size: {fileSize / 1024.0 / 1024.0:F1} MB");
        Console.WriteLine();

        var connectionString = $"Data Source={dbPath};Mode=ReadOnly";
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        // Total rows and date range
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT
                    COUNT(*) as total_rows,
                    MIN(fetched_at) as oldest,
                    MAX(fetched_at) as newest,
                    AVG(CASE WHEN details_json IS NOT NULL THEN LENGTH(details_json) ELSE 0 END) as avg_details_bytes,
                    SUM(CASE WHEN details_json IS NOT NULL THEN LENGTH(details_json) ELSE 0 END) as total_details_bytes,
                    COUNT(DISTINCT provider_id) as provider_count
                FROM provider_history";
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                Console.WriteLine($"Total rows:      {reader.GetInt64(0):N0}");
                Console.WriteLine($"Oldest entry:    {reader.GetString(1)}");
                Console.WriteLine($"Newest entry:    {reader.GetString(2)}");
                Console.WriteLine($"Providers:       {reader.GetInt64(5)}");
                Console.WriteLine($"Avg details_json:{reader.GetDouble(3):F0} bytes");
                Console.WriteLine($"Total details:   {reader.GetInt64(4) / 1024.0 / 1024.0:F1} MB");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Rows per provider:");
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT provider_id, COUNT(*) as cnt,
                       MIN(fetched_at) as oldest,
                       MAX(fetched_at) as newest
                FROM provider_history
                GROUP BY provider_id
                ORDER BY cnt DESC";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                Console.WriteLine($"  {reader.GetString(0),-30} {reader.GetInt64(1),8:N0}  ({reader.GetString(2)[..10]} → {reader.GetString(3)[..10]})");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Rows per day (last 30 days):");
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT DATE(fetched_at) as day, COUNT(*) as cnt
                FROM provider_history
                WHERE fetched_at >= datetime('now', '-30 days')
                GROUP BY day
                ORDER BY day DESC";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                Console.WriteLine($"  {reader.GetString(0)}  {reader.GetInt64(1),6:N0}");
            }
        }

        Console.WriteLine();
        Console.WriteLine("Rows per month (older than 30 days):");
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT strftime('%Y-%m', fetched_at) as month, COUNT(*) as cnt
                FROM provider_history
                WHERE fetched_at < datetime('now', '-30 days')
                GROUP BY month
                ORDER BY month DESC";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                Console.WriteLine($"  {reader.GetString(0)}  {reader.GetInt64(1),8:N0}");
            }
        }

        return 0;
    }

    private static int ExportData(string outputPath)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dbPath = Path.Combine(appData, "AIUsageTracker", "usage.db");

        if (!File.Exists(dbPath))
        {
            Console.Error.WriteLine($"Database not found: {dbPath}");
            return 1;
        }

        Console.WriteLine($"Reading from: {dbPath}");

        var connectionString = $"Data Source={dbPath}";
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        var providers = LoadProvidersFromDatabase(connection);
        var latestHistory = LoadLatestHistoryFromDatabase(connection);
        var history7Days = LoadHistory7DaysFromDatabase(connection);

        var fixture = new TestDataFixture
        {
            ExportedAt = DateTime.UtcNow.ToString("o"),
            Providers = providers,
            LatestHistory = latestHistory,
            History7Days = history7Days,
        };

        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        WriteTestFixtureToFile(fixture, outputPath);

        Console.WriteLine($"Exported to: {outputPath}");
        Console.WriteLine($"  Providers: {providers.Count}");
        Console.WriteLine($"  Latest history: {latestHistory.Count}");
        Console.WriteLine($"  7-day history: {history7Days.Count}");

        return 0;
    }

    private static List<ProviderFixture> LoadProvidersFromDatabase(SqliteConnection connection)
    {
        var providers = new List<ProviderFixture>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT provider_id, provider_name, account_name, is_active, config_json FROM providers";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                providers.Add(new ProviderFixture
                {
                    ProviderId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    ProviderName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    AccountName = reader.IsDBNull(2) ? null : reader.GetString(2),
                    IsActive = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                    ConfigJson = reader.IsDBNull(4) ? null : reader.GetString(4),
                });
            }
        }

        return providers;
    }

    private static List<HistoryFixture> LoadLatestHistoryFromDatabase(SqliteConnection connection)
    {
        var latestHistory = new List<HistoryFixture>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT h.provider_id, h.requests_used, h.requests_available, h.requests_percentage, 
                       h.is_available, h.status_message, h.next_reset_time, h.fetched_at, h.details_json,
                       h.response_latency_ms
                FROM provider_history h
                INNER JOIN (
                    SELECT provider_id, MAX(fetched_at) as max_fetched
                    FROM provider_history
                    GROUP BY provider_id
                ) latest ON h.provider_id = latest.provider_id AND h.fetched_at = latest.max_fetched
                ORDER BY h.provider_id";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                latestHistory.Add(ReadHistoryFixture(reader));
            }
        }

        return latestHistory;
    }

    private static List<HistoryFixture> LoadHistory7DaysFromDatabase(SqliteConnection connection)
    {
        var history7Days = new List<HistoryFixture>();
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT provider_id, requests_used, requests_available, requests_percentage,
                       is_available, status_message, next_reset_time, fetched_at, details_json,
                       response_latency_ms
                FROM provider_history
                WHERE fetched_at >= datetime('now', '-7 days')
                ORDER BY provider_id, fetched_at DESC";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                history7Days.Add(ReadHistoryFixture(reader));
            }
        }

        return history7Days;
    }

    private static HistoryFixture ReadHistoryFixture(SqliteDataReader reader)
    {
        return new HistoryFixture
        {
            ProviderId = reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
            RequestsUsed = reader.IsDBNull(1) ? 0 : reader.GetDouble(1),
            RequestsAvailable = reader.IsDBNull(2) ? 0 : reader.GetDouble(2),
            RequestsPercentage = reader.IsDBNull(3) ? 0 : reader.GetDouble(3),
            IsAvailable = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
            StatusMessage = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
            NextResetTime = reader.IsDBNull(6) ? null : reader.GetString(6),
            FetchedAt = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
            DetailsJson = reader.IsDBNull(8) ? null : reader.GetString(8),
            ResponseLatencyMs = reader.IsDBNull(9) ? 0 : reader.GetDouble(9),
        };
    }

    private static void WriteTestFixtureToFile(TestDataFixture fixture, string outputPath)
    {
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        var json = JsonSerializer.Serialize(fixture, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(outputPath, json);

        Console.WriteLine($"Exported to: {outputPath}");
        Console.WriteLine($"  Providers: {fixture.Providers.Count}");
        Console.WriteLine($"  Latest history: {fixture.LatestHistory.Count}");
        Console.WriteLine($"  7-day history: {fixture.History7Days.Count}");
    }

    private static int SeedDatabase(string fixturePath)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dbDir = Path.Combine(appData, "AIUsageTracker");
        Directory.CreateDirectory(dbDir);
        var dbPath = Path.Combine(dbDir, "usage.db");

        if (File.Exists(dbPath))
        {
            File.Delete(dbPath);
        }

        Console.WriteLine($"Creating database at: {dbPath}");

        if (!File.Exists(fixturePath))
        {
            Console.Error.WriteLine($"Fixture file not found: {fixturePath}");
            Console.Error.WriteLine("Run scripts/export-provider-data.ps1 on a machine with real provider data first.");
            return 1;
        }

        Console.WriteLine($"Loading fixture from: {fixturePath}");
        var json = File.ReadAllText(fixturePath);
        var fixture = JsonSerializer.Deserialize<TestDataFixture>(json);

        if (fixture == null || fixture.Providers.Count == 0)
        {
            Console.Error.WriteLine("Fixture is empty or invalid.");
            return 1;
        }

        Console.WriteLine($"Fixture contains {fixture.Providers.Count} providers");

        var connectionString = $"Data Source={dbPath}";
        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        connection.Execute(@"
            CREATE TABLE providers (
                provider_id TEXT PRIMARY KEY,
                provider_name TEXT,
                account_name TEXT,
                auth_source TEXT,
                updated_at TEXT,
                is_active INTEGER,
                config_json TEXT
            );

            CREATE TABLE provider_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                provider_id TEXT,
                requests_used REAL,
                requests_available REAL,
                requests_percentage REAL,
                is_available INTEGER,
                status_message TEXT,
                next_reset_time TEXT,
                fetched_at TEXT,
                details_json TEXT,
                response_latency_ms REAL NOT NULL DEFAULT 0
            );

            CREATE TABLE reset_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                provider_id TEXT,
                provider_name TEXT,
                previous_usage REAL,
                new_usage REAL,
                reset_type TEXT,
                timestamp TEXT
            );

            CREATE TABLE raw_snapshots (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                provider_id TEXT,
                raw_json TEXT,
                http_status INTEGER,
                fetched_at TEXT
            );
        ");

        foreach (var provider in fixture.Providers)
        {
            connection.Execute(
                @"
                INSERT INTO providers (provider_id, provider_name, account_name, is_active, updated_at, config_json)
                VALUES (@Id, @Name, @Account, @IsActive, CURRENT_TIMESTAMP, @Config)",
                new
                {
                    Id = provider.ProviderId,
                    Name = provider.ProviderName,
                    Account = provider.AccountName,
                    IsActive = provider.IsActive,
                    Config = provider.ConfigJson,
                });
        }

        var historyToInsert = fixture.LatestHistory.Count > 0 ? fixture.LatestHistory : fixture.History7Days;
        foreach (var history in historyToInsert)
        {
            connection.Execute(
                @"
                INSERT INTO provider_history (provider_id, requests_used, requests_available, requests_percentage, is_available, status_message, next_reset_time, fetched_at, details_json, response_latency_ms)
                VALUES (@Id, @Used, @Avail, @Perc, @IsAvail, @Msg, @Next, @Fetched, @Details, @Latency)",
                new
                {
                    Id = history.ProviderId,
                    Used = history.RequestsUsed,
                    Avail = history.RequestsAvailable,
#pragma warning disable CS0618 // RequestsPercentage: database seeder pass-through
                    Perc = history.RequestsPercentage,
#pragma warning restore CS0618
                    IsAvail = history.IsAvailable,
                    Msg = history.StatusMessage,
                    Next = history.NextResetTime,
                    Fetched = history.FetchedAt,
                    Details = history.DetailsJson,
                    Latency = history.ResponseLatencyMs,
                });
        }

        Console.WriteLine($"Seeded {fixture.Providers.Count} providers with {historyToInsert.Count} history records.");
        return 0;
    }

    private sealed class ProviderFixture
    {
        [JsonPropertyName("provider_id")]
        public string ProviderId { get; set; } = string.Empty;

        [JsonPropertyName("provider_name")]
        public string ProviderName { get; set; } = string.Empty;

        [JsonPropertyName("account_name")]
        public string? AccountName { get; set; }

        [JsonPropertyName("is_active")]
        public int IsActive { get; set; }

        [JsonPropertyName("config_json")]
        public string? ConfigJson { get; set; }
    }

    private sealed class HistoryFixture
    {
        [JsonPropertyName("provider_id")]
        public string ProviderId { get; set; } = string.Empty;

        [JsonPropertyName("requests_used")]
        public double RequestsUsed { get; set; }

        [JsonPropertyName("requests_available")]
        public double RequestsAvailable { get; set; }

        [JsonPropertyName("requests_percentage")]
        public double RequestsPercentage { get; set; }

        [JsonPropertyName("is_available")]
        public int IsAvailable { get; set; }

        [JsonPropertyName("status_message")]
        public string StatusMessage { get; set; } = string.Empty;

        [JsonPropertyName("next_reset_time")]
        public string? NextResetTime { get; set; }

        [JsonPropertyName("fetched_at")]
        public string FetchedAt { get; set; } = string.Empty;

        [JsonPropertyName("details_json")]
        public string? DetailsJson { get; set; }

        [JsonPropertyName("response_latency_ms")]
        public double ResponseLatencyMs { get; set; }
    }

    private sealed class TestDataFixture
    {
        [JsonPropertyName("exported_at")]
        public string ExportedAt { get; set; } = string.Empty;

        [JsonPropertyName("providers")]
        public List<ProviderFixture> Providers { get; set; } = [];

        [JsonPropertyName("latest_history")]
        public List<HistoryFixture> LatestHistory { get; set; } = [];

        [JsonPropertyName("history_7days")]
        public List<HistoryFixture> History7Days { get; set; } = [];
    }
}
