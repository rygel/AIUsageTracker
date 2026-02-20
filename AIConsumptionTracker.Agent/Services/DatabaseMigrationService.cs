using EvolveDb;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace AIConsumptionTracker.Agent.Services;

public class DatabaseMigrationService
{
    private readonly string _connectionString;
    private readonly ILogger<DatabaseMigrationService> _logger;

    public DatabaseMigrationService(string dbPath, ILogger<DatabaseMigrationService> logger)
    {
        _connectionString = $"Data Source={dbPath}";
        _logger = logger;
    }

    public void RunMigrations()
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            var evolve = new Evolve(connection, msg => _logger.LogInformation("{Message}", msg))
            {
                EmbeddedResourceAssemblies = new[] { typeof(DatabaseMigrationService).Assembly },
                EmbeddedResourceFilters = new[] { "AIConsumptionTracker.Agent.Migrations" }
            };

            evolve.Migrate();

            _logger.LogInformation("DB migrated ({Count} applied)", evolve.NbMigration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database migration failed: {Message}", ex.Message);
            throw;
        }
    }
}
