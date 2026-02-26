using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using AIUsageTracker.Core.Interfaces;
using AIUsageTracker.Core.Models;

namespace AIUsageTracker.Infrastructure.Providers;

/// <summary>
/// Evolve database migration tracking provider
/// Reads from Evolve's changelog table to display migration history
/// </summary>
public class EvolveMigrationProvider : IProviderService
{
    public string ProviderId => "evolve-migrations";
    private readonly ILogger<EvolveMigrationProvider> _logger;

    public EvolveMigrationProvider(ILogger<EvolveMigrationProvider> logger)
    {
        _logger = logger;
    }

    public async Task<IEnumerable<ProviderUsage>> GetUsageAsync(ProviderConfig config, Action<ProviderUsage>? progressCallback = null)
    {
        try
        {
            // Get the database path from config
            var dbPath = config.BaseUrl ?? GetDefaultDbPath();
            
            if (string.IsNullOrEmpty(dbPath) || !File.Exists(dbPath))
            {
                return new[] { new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "Evolve Migrations",
                    IsAvailable = false,
                    Description = "Database not found"
                }};
            }

            using var connection = new SqliteConnection($"Data Source={dbPath}");
            await connection.OpenAsync();

            // Query Evolve's changelog table
            var migrations = await GetMigrationsAsync(connection);
            
            if (migrations.Count == 0)
            {
                return new[] { new ProviderUsage
                {
                    ProviderId = ProviderId,
                    ProviderName = "Evolve Migrations",
                    IsAvailable = true,
                    Description = "No migrations found"
                }};
            }

            // Calculate stats
            var latestMigration = migrations.First();
            var totalMigrations = migrations.Count;
            var successfulMigrations = migrations.Count(m => m.Success);
            var failedMigrations = migrations.Count(m => !m.Success);
            
            // Create summary
            var description = $"Total: {totalMigrations}, Success: {successfulMigrations}, Failed: {failedMigrations}";
            if (failedMigrations > 0)
            {
                description += $" ⚠️ Last failed: {migrations.First(m => !m.Success).Version}";
            }
            else
            {
                description += $" ✓ Latest: {latestMigration.Version}";
            }

            return new[] { new ProviderUsage
            {
                ProviderId = ProviderId,
                ProviderName = "Evolve Migrations",
                RequestsPercentage = failedMigrations > 0 ? 100 : 0, // Red if failures
                RequestsUsed = failedMigrations,
                RequestsAvailable = totalMigrations,
                PlanType = PlanType.Usage,
                UsageUnit = "Migrations",
                IsQuotaBased = false,
                IsAvailable = true,
                Description = description,
                Details = migrations.Select(m => new ProviderUsageDetail
                {
                    Name = $"v{m.Version}",
                    Used = m.Success ? "✓" : "✗",
                    Description = $"{m.Description} ({m.Duration.TotalSeconds:F1}s) - {m.AppliedAt:yyyy-MM-dd HH:mm}"
                }).ToList()
            }};
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading Evolve migrations");
            return new[] { new ProviderUsage
            {
                ProviderId = ProviderId,
                ProviderName = "Evolve Migrations",
                IsAvailable = false,
                Description = $"Error: {ex.Message}"
            }};
        }
    }

    private async Task<List<EvolveMigration>> GetMigrationsAsync(SqliteConnection connection)
    {
        var migrations = new List<EvolveMigration>();
        
        // Try different possible table names
        var tableNames = new[] { "changelog", "evolve", "evolve_changelog", "EvolveChangelog" };
        
        foreach (var tableName in tableNames)
        {
            try
            {
                var cmd = connection.CreateCommand();
                cmd.CommandText = $@"
                    SELECT 
                        version,
                        description,
                        installed_on_utc as applied_at,
                        success,
                        execution_time_ms as duration_ms
                    FROM {tableName}
                    ORDER BY installed_on_utc DESC
                    LIMIT 50";

                using var reader = await cmd.ExecuteReaderAsync();
                
                while (await reader.ReadAsync())
                {
                    migrations.Add(new EvolveMigration
                    {
                        Version = reader.GetString(0),
                        Description = reader.GetString(1),
                        AppliedAt = reader.GetDateTime(2),
                        Success = reader.GetInt32(3) == 1,
                        Duration = TimeSpan.FromMilliseconds(reader.GetInt32(4))
                    });
                }

                if (migrations.Count > 0)
                    break; // Found the table
            }
            catch
            {
                // Table doesn't exist or wrong schema, try next
                continue;
            }
        }

        return migrations;
    }

    private string GetDefaultDbPath()
    {
        // Look for the Monitor database (with legacy fallback)
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var primaryDbPath = Path.Combine(appData, "AIUsageTracker", "usage.db");
        if (File.Exists(primaryDbPath))
        {
            return primaryDbPath;
        }

        var legacyDbPath = Path.Combine(appData, "AIConsumptionTracker", "usage.db");
        return File.Exists(legacyDbPath) ? legacyDbPath : string.Empty;
    }

    private class EvolveMigration
    {
        public string Version { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime AppliedAt { get; set; }
        public bool Success { get; set; }
        public TimeSpan Duration { get; set; }
    }
}

