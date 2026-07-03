using Microsoft.Data.Sqlite;

namespace Orderly.Data.Sqlite;

/// <summary>
/// Idempotent schema initialization for the client-side cloud cache and emergency draft tables.
/// These tables live in the same per-workspace SQLCipher database as the legacy CRM and Commerce
/// tables so the existing account data key naturally encrypts offline data.
/// </summary>
public static class CloudCacheSchemaInitializer
{
    public static async Task InitializeSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(connection, "PRAGMA journal_mode=WAL;", cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS CloudCacheEntries (
                EntityType TEXT NOT NULL,
                EntityId TEXT NOT NULL,
                PayloadJson TEXT NOT NULL,
                Revision INTEGER NOT NULL DEFAULT 0,
                CachedAtUtc TEXT NOT NULL,
                PRIMARY KEY (EntityType, EntityId)
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE INDEX IF NOT EXISTS IX_CloudCacheEntries_EntityType ON CloudCacheEntries (EntityType);
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS EmergencyDrafts (
                Id TEXT PRIMARY KEY,
                EntityType TEXT NOT NULL,
                EntityId TEXT NULL,
                OperationType TEXT NOT NULL,
                PayloadJson TEXT NOT NULL,
                BaseRevision INTEGER NULL,
                CreatedAtUtc TEXT NOT NULL,
                Status TEXT NOT NULL,
                LastSubmitError TEXT NULL
            );
            """, cancellationToken);

        await ExecuteAsync(connection, """
            CREATE INDEX IF NOT EXISTS IX_EmergencyDrafts_Status ON EmergencyDrafts (Status, CreatedAtUtc);
            """, cancellationToken);
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string commandText, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
