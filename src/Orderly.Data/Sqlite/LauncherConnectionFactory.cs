using Microsoft.Data.Sqlite;

namespace Orderly.Data.Sqlite;

public sealed class LauncherConnectionFactory
{
    private readonly string _connectionString;

    public LauncherConnectionFactory(string launcherDatabasePath)
    {
        DatabasePath = launcherDatabasePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = launcherDatabasePath,
            ForeignKeys = true
        }.ToString();
    }

    public string DatabasePath { get; }

    public SqliteConnection CreateConnection()
    {
        return new SqliteConnection(_connectionString);
    }
}
