using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Data.Sqlite;
using System.Globalization;

namespace Orderly.Data.Repositories;

public sealed class SyncRecordRepository : ISyncRecordRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SyncRecordRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<SyncRecord> CreateAsync(SyncRecord record, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        if (record.CreatedAt == default)
        {
            record.CreatedAt = now;
        }

        record.UpdatedAt = now;
        record.DeletedAt = null;
        record.IsSynced = record.SyncStatus == SyncStatus.Synced;
        record.Version = Math.Max(1, record.Version);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO SyncRecords (
                EntityType, EntityId, RemoteId, SyncStatus, LastSyncedAt, ErrorMessage, MetadataJson,
                CreatedAt, UpdatedAt, DeletedAt, IsSynced, Version
            )
            VALUES (
                $entityType, $entityId, $remoteId, $syncStatus, $lastSyncedAt, $errorMessage, $metadataJson,
                $createdAt, $updatedAt, $deletedAt, $isSynced, $version
            );
            SELECT last_insert_rowid();
            """;
        AddParameters(command, record);
        record.Id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return await GetByIdAsync(record.Id, cancellationToken) ?? record;
    }

    public async Task UpdateAsync(SyncRecord record, CancellationToken cancellationToken = default)
    {
        record.UpdatedAt = DateTime.Now;
        record.IsSynced = record.SyncStatus == SyncStatus.Synced;
        record.Version = Math.Max(1, record.Version + 1);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE SyncRecords
            SET EntityType = $entityType,
                EntityId = $entityId,
                RemoteId = $remoteId,
                SyncStatus = $syncStatus,
                LastSyncedAt = $lastSyncedAt,
                ErrorMessage = $errorMessage,
                MetadataJson = $metadataJson,
                UpdatedAt = $updatedAt,
                DeletedAt = $deletedAt,
                IsSynced = $isSynced,
                Version = $version
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        AddParameters(command, record);
        command.Parameters.AddWithValue("$id", record.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SyncRecord?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                Id, EntityType, EntityId, RemoteId, SyncStatus, LastSyncedAt, ErrorMessage, MetadataJson,
                CreatedAt, UpdatedAt, DeletedAt, IsSynced, Version
            FROM SyncRecords
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<SyncRecord?> GetByEntityAsync(string entityType, int entityId, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                Id, EntityType, EntityId, RemoteId, SyncStatus, LastSyncedAt, ErrorMessage, MetadataJson,
                CreatedAt, UpdatedAt, DeletedAt, IsSynced, Version
            FROM SyncRecords
            WHERE EntityType = $entityType AND EntityId = $entityId AND DeletedAt IS NULL
            ORDER BY UpdatedAt DESC, Id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$entityType", entityType);
        command.Parameters.AddWithValue("$entityId", entityId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<IReadOnlyList<SyncRecord>> ListPendingAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                Id, EntityType, EntityId, RemoteId, SyncStatus, LastSyncedAt, ErrorMessage, MetadataJson,
                CreatedAt, UpdatedAt, DeletedAt, IsSynced, Version
            FROM SyncRecords
            WHERE DeletedAt IS NULL AND SyncStatus = $syncStatus
            ORDER BY UpdatedAt DESC, Id DESC;
            """;
        command.Parameters.AddWithValue("$syncStatus", (int)SyncStatus.Pending);

        var rows = new List<SyncRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Map(reader));
        }

        return rows;
    }

    private static void AddParameters(SqliteCommand command, SyncRecord record)
    {
        command.Parameters.AddWithValue("$entityType", record.EntityType);
        command.Parameters.AddWithValue("$entityId", record.EntityId);
        command.Parameters.AddWithValue("$remoteId", record.RemoteId);
        command.Parameters.AddWithValue("$syncStatus", (int)record.SyncStatus);
        command.Parameters.AddWithValue("$lastSyncedAt", ToDbDate(record.LastSyncedAt));
        command.Parameters.AddWithValue("$errorMessage", record.ErrorMessage);
        command.Parameters.AddWithValue("$metadataJson", record.MetadataJson);
        command.Parameters.AddWithValue("$createdAt", record.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", record.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$deletedAt", ToDbDate(record.DeletedAt));
        command.Parameters.AddWithValue("$isSynced", record.IsSynced ? 1 : 0);
        command.Parameters.AddWithValue("$version", record.Version);
    }

    private static SyncRecord Map(SqliteDataReader reader)
    {
        return new SyncRecord
        {
            Id = reader.GetInt32(0),
            EntityType = reader.GetString(1),
            EntityId = reader.GetInt32(2),
            RemoteId = reader.GetString(3),
            SyncStatus = (SyncStatus)reader.GetInt32(4),
            LastSyncedAt = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5), null, DateTimeStyles.RoundtripKind),
            ErrorMessage = reader.GetString(6),
            MetadataJson = reader.GetString(7),
            CreatedAt = DateTime.Parse(reader.GetString(8), null, DateTimeStyles.RoundtripKind),
            UpdatedAt = DateTime.Parse(reader.GetString(9), null, DateTimeStyles.RoundtripKind),
            DeletedAt = reader.IsDBNull(10) ? null : DateTime.Parse(reader.GetString(10), null, DateTimeStyles.RoundtripKind),
            IsSynced = reader.GetInt32(11) == 1,
            Version = reader.GetInt32(12)
        };
    }

    private static object ToDbDate(DateTime? value)
    {
        return value is null ? DBNull.Value : value.Value.ToString("O");
    }
}
