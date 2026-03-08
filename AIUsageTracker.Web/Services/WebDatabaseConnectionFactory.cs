using Microsoft.Data.Sqlite;

namespace AIUsageTracker.Web.Services;

internal sealed class WebDatabaseConnectionFactory
{
    private readonly string _databasePath;
    private readonly string _readConnectionString;

    public WebDatabaseConnectionFactory(string databasePath)
    {
        this._databasePath = databasePath;
        this._readConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = this._databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
            DefaultTimeout = 10,
        }.ToString();
    }

    public bool IsDatabaseAvailable()
    {
        return File.Exists(this._databasePath);
    }

    public string GetDatabasePath()
    {
        return this._databasePath;
    }

    public SqliteConnection CreateReadConnection()
    {
        return new SqliteConnection(this._readConnectionString);
    }
}
