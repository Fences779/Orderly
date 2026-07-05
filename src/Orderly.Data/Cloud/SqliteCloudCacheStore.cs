using Microsoft.Data.Sqlite;
using Orderly.Contracts.Offline;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Cloud;

/// <summary>
/// SQLCipher-backed implementation of <see cref="ICloudCacheStore"/>.
/// Stores cloud entity snapshots in the per-workspace encrypted database so the client can read
/// previously fetched data while offline.
/// </summary>
public sealed class SqliteCloudCacheStore : ICloudCacheStore
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteCloudCacheStore(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<CloudCacheEntryDto?> GetAsync(string entityType, string entityId, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EntityType, EntityId, PayloadJson, Revision, CachedAtUtc
            FROM CloudCacheEntries
            WHERE EntityType = $entityType AND EntityId = $entityId;
            """;
        command.Parameters.AddWithValue("$entityType", entityType);
        command.Parameters.AddWithValue("$entityId", entityId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return MapEntry(reader);
    }

    public async Task SetAsync(CloudCacheEntryDto entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await UpsertAsync(connection, null, entry, cancellationToken);
    }

    public async Task ReplaceAllAsync(IEnumerable<CloudCacheEntryDto> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var stagedEntries = entries.ToList();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction();

        await using (var createStage = connection.CreateCommand())
        {
            createStage.Transaction = transaction;
            createStage.CommandText = """
                CREATE TEMP TABLE IF NOT EXISTS CloudCacheEntriesStage (
                    EntityType TEXT NOT NULL,
                    EntityId TEXT NOT NULL,
                    PayloadJson TEXT NOT NULL,
                    Revision INTEGER NOT NULL DEFAULT 0,
                    CachedAtUtc TEXT NOT NULL,
                    PRIMARY KEY (EntityType, EntityId)
                );
                """;
            await createStage.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var clearStage = connection.CreateCommand())
        {
            clearStage.Transaction = transaction;
            clearStage.CommandText = "DELETE FROM CloudCacheEntriesStage;";
            await clearStage.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var entry in stagedEntries)
        {
            await UpsertAsync(connection, transaction, "CloudCacheEntriesStage", entry, cancellationToken);
        }

        await using (var replace = connection.CreateCommand())
        {
            replace.Transaction = transaction;
            replace.CommandText = """
                DELETE FROM CloudCacheEntries;
                INSERT INTO CloudCacheEntries (EntityType, EntityId, PayloadJson, Revision, CachedAtUtc)
                SELECT EntityType, EntityId, PayloadJson, Revision, CachedAtUtc
                FROM CloudCacheEntriesStage;
                """;
            await replace.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task UpsertAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        CloudCacheEntryDto entry,
        CancellationToken cancellationToken)
        => await UpsertAsync(connection, transaction, "CloudCacheEntries", entry, cancellationToken);

    private static async Task UpsertAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string tableName,
        CloudCacheEntryDto entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            INSERT INTO {tableName} (EntityType, EntityId, PayloadJson, Revision, CachedAtUtc)
            VALUES ($entityType, $entityId, $payloadJson, $revision, $cachedAtUtc)
            ON CONFLICT(EntityType, EntityId) DO UPDATE SET
                PayloadJson = excluded.PayloadJson,
                Revision = excluded.Revision,
                CachedAtUtc = excluded.CachedAtUtc;
            """;
        command.Parameters.AddWithValue("$entityType", entry.EntityType);
        command.Parameters.AddWithValue("$entityId", entry.EntityId);
        command.Parameters.AddWithValue("$payloadJson", entry.PayloadJson);
        command.Parameters.AddWithValue("$revision", entry.Revision);
        command.Parameters.AddWithValue("$cachedAtUtc", entry.CachedAtUtc.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemoveAsync(string entityType, string entityId, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM CloudCacheEntries
            WHERE EntityType = $entityType AND EntityId = $entityId;
            """;
        command.Parameters.AddWithValue("$entityType", entityType);
        command.Parameters.AddWithValue("$entityId", entityId);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CloudCacheEntryDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var entries = new List<CloudCacheEntryDto>();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EntityType, EntityId, PayloadJson, Revision, CachedAtUtc
            FROM CloudCacheEntries
            ORDER BY EntityType, EntityId;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(MapEntry(reader));
        }

        return entries;
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM CloudCacheEntries;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static CloudCacheEntryDto MapEntry(SqliteDataReader reader)
    {
        return new CloudCacheEntryDto
        {
            EntityType = reader.GetString(0),
            EntityId = reader.GetString(1),
            PayloadJson = reader.GetString(2),
            Revision = reader.GetInt64(3),
            CachedAtUtc = reader.GetDateTime(4)
        };
    }
}
