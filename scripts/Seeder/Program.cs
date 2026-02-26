using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Dapper;

namespace Seeder;

class Program
{
    static void Main(string[] args)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dbDir = Path.Combine(appData, "AIUsageTracker");
        Directory.CreateDirectory(dbDir);
        var dbPath = Path.Combine(dbDir, "usage.db");

        if (File.Exists(dbPath))
        {
            File.Delete(dbPath);
        }

        Console.WriteLine($"Creating mock database at: {dbPath}");
        var connectionString = $"Data Source={dbPath}";

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        // 1. Schema
        connection.Execute(@"
            CREATE TABLE providers (
                provider_id TEXT PRIMARY KEY,
                provider_name TEXT,
                plan_type TEXT,
                auth_source TEXT,
                account_name TEXT,
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
                details_json TEXT
            );

            CREATE TABLE reset_events (
                id TEXT PRIMARY KEY,
                provider_id TEXT,
                provider_name TEXT,
                previous_usage REAL,
                new_usage REAL,
                reset_type TEXT,
                timestamp TEXT
            );
        ");

        // Helper
        var now = DateTime.UtcNow;
        
        void AddProvider(string id, string name, string plan, string auth, bool active)
        {
            connection.Execute(@"
                INSERT INTO providers (provider_id, provider_name, plan_type, auth_source, is_active, updated_at) 
                VALUES (@Id, @Name, @Plan, @Auth, @IsActive, CURRENT_TIMESTAMP)", 
                new { Id = id, Name = name, Plan = plan, Auth = auth, IsActive = active ? 1 : 0 });
        }

        void AddHistory(string id, double used, double avail, double perc, bool isAvail, string msg, int? nextResetHours, int hoursAgo, string detailsJson = null)
        {
            var fetched = now.AddHours(-hoursAgo).ToString("yyyy-MM-dd HH:mm:ss");
            string nextReset = nextResetHours.HasValue ? now.AddHours(nextResetHours.Value).ToString("yyyy-MM-dd HH:mm:ss") : null;

            connection.Execute(@"
                INSERT INTO provider_history (provider_id, requests_used, requests_available, requests_percentage, is_available, status_message, next_reset_time, fetched_at, details_json) 
                VALUES (@Id, @Used, @Avail, @Perc, @IsAvail, @Msg, @Next, @Fetched, @Details)",
                new { Id = id, Used = used, Avail = avail, Perc = perc, IsAvail = isAvail, Msg = msg, Next = nextReset, Fetched = fetched, Details = detailsJson });
        }

        // --- Data ---
        AddProvider("antigravity", "Antigravity", "coding", "local app", true);
        var sgDetails = "[{\"Name\":\"Claude Opus 4.6 (Thinking)\",\"ModelName\":\"Claude Opus 4.6 (Thinking)\",\"GroupName\":\"Recommended Group 1\",\"Used\":\"60%\",\"Description\":\"60% remaining\"},{\"Name\":\"Gemini 3 Flash\",\"ModelName\":\"Gemini 3 Flash\",\"GroupName\":\"Recommended Group 1\",\"Used\":\"100%\",\"Description\":\"100% remaining\"}]";
        AddHistory("antigravity", 40, 100, 60.0, true, "60.0% Remaining", 10, 0, sgDetails);
        AddHistory("antigravity", 38, 100, 62.0, true, "62.0% Remaining", 10, 2);
        AddHistory("antigravity", 35, 100, 65.0, true, "65.0% Remaining", 10, 4);

        AddProvider("github-copilot", "GitHub Copilot", "coding", "oauth", true);
        AddHistory("github-copilot", 110, 400, 72.5, true, "72.5% Remaining", 20, 0);
        AddHistory("github-copilot", 105, 400, 73.7, true, "73.7% Remaining", 20, 2);
        AddHistory("github-copilot", 100, 400, 75.0, true, "75.0% Remaining", 20, 4);

        AddProvider("zai-coding-plan", "Z.AI", "coding", "api key", true);
        AddHistory("zai-coding-plan", 45, 250, 82.0, true, "82.0% Remaining", 12, 0);
        AddHistory("zai-coding-plan", 40, 250, 84.0, true, "84.0% Remaining", 12, 2);
        
        AddProvider("synthetic", "Synthetic", "coding", "api key", true);
        AddHistory("synthetic", 18, 200, 91.0, true, "91.0% Remaining", 4, 0);

        AddProvider("claude-code", "Claude Code", "usage", "local credentials", true);
        AddHistory("claude-code", 0, 0, 0, true, "Connected", null, 0);

        AddProvider("mistral", "Mistral", "usage", "api key", true);
        AddHistory("mistral", 0, 0, 0, true, "Connected", null, 0);

        AddProvider("openai", "OpenAI", "usage", "api key", true);
        AddHistory("openai", 12.45, 40.0, 31.1, true, "$12.45 / $40.00", null, 0);
        AddHistory("openai", 10.00, 40.0, 25.0, true, "$10.00 / $40.00", null, 8);
        AddHistory("openai", 5.50, 40.0, 13.7, true, "$5.50 / $40.00", null, 16);

        Console.WriteLine("Seeded sqlite database successfully.");
    }
}
