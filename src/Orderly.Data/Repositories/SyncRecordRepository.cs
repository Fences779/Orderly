using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using Orderly.Data.Sqlite;
using System.Globalization;

namespace Orderly.Data.Repositories;

public sealed class SyncRecordRepository : ISyncRecordRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly IFieldEncryptionService _fieldEncryptionService;

    public SyncRecordRepository(SqliteConnectionFactory connectionFactory, IFieldEncryptionService fieldEncryptionService)
    {
        _connectionFactory = connectionFactory;
        _fieldEncryptionService = fieldEncryptionService ?? throw new ArgumentNullException(nameof(fieldEncryptionService));
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
                EntityType, EntityId, RemoteId, SyncStatus, LastSyncedAt, ErrorMessage, ErrorMessageCiphertext, MetadataJson, MetadataJsonCiphertext,
                CreatedAt, UpdatedAt, DeletedAt, IsSynced, Version
            )
            VALUES (
                $entityType, $entityId, $remoteId, $syncStatus, $lastSyncedAt, $errorMessage, $errorMessageCiphertext, $metadataJson, $metadataJsonCiphertext,
                $createdAt, $updatedAt, $deletedAt, $isSynced, $version
            );
            SELECT last_insert_rowid();
            """;
        AddParameters(command, record, _fieldEncryptionService);
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
                ErrorMessageCiphertext = $errorMessageCiphertext,
                MetadataJson = $metadataJson,
                MetadataJsonCiphertext = $metadataJsonCiphertext,
                UpdatedAt = $updatedAt,
                DeletedAt = $deletedAt,
                IsSynced = $isSynced,
                Version = $version
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        AddParameters(command, record, _fieldEncryptionService);
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
                Id, EntityType, EntityId, RemoteId, SyncStatus, LastSyncedAt, ErrorMessage, ErrorMessageCiphertext, MetadataJson, MetadataJsonCiphertext,
                CreatedAt, UpdatedAt, DeletedAt, IsSynced, Version
            FROM SyncRecords
            WHERE Id = $id AND DeletedAt IS NULL;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader, _fieldEncryptionService) : null;
    }

    public async Task<SyncRecord?> GetByEntityAsync(string entityType, int entityId, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                Id, EntityType, EntityId, RemoteId, SyncStatus, LastSyncedAt, ErrorMessage, ErrorMessageCiphertext, MetadataJson, MetadataJsonCiphertext,
                CreatedAt, UpdatedAt, DeletedAt, IsSynced, Version
            FROM SyncRecords
            WHERE EntityType = $entityType AND EntityId = $entityId AND DeletedAt IS NULL
            ORDER BY UpdatedAt DESC, Id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$entityType", entityType);
        command.Parameters.AddWithValue("$entityId", entityId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader, _fieldEncryptionService) : null;
    }

    public async Task<SyncRecord?> GetLatestByEntityTypeAsync(string entityType, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                Id, EntityType, EntityId, RemoteId, SyncStatus, LastSyncedAt, ErrorMessage, ErrorMessageCiphertext, MetadataJson, MetadataJsonCiphertext,
                CreatedAt, UpdatedAt, DeletedAt, IsSynced, Version
            FROM SyncRecords
            WHERE EntityType = $entityType AND DeletedAt IS NULL
            ORDER BY UpdatedAt DESC, Id DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$entityType", entityType);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader, _fieldEncryptionService) : null;
    }

    public async Task<IReadOnlyList<SyncRecord>> ListPendingAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                Id, EntityType, EntityId, RemoteId, SyncStatus, LastSyncedAt, ErrorMessage, ErrorMessageCiphertext, MetadataJson, MetadataJsonCiphertext,
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
            rows.Add(Map(reader, _fieldEncryptionService));
        }

        return rows;
    }

    private static void AddParameters(SqliteCommand command, SyncRecord record, IFieldEncryptionService fieldEncryptionService)
    {
        command.Parameters.AddWithValue("$entityType", record.EntityType);
        command.Parameters.AddWithValue("$entityId", record.EntityId);
        command.Parameters.AddWithValue("$remoteId", record.RemoteId);
        command.Parameters.AddWithValue("$syncStatus", (int)record.SyncStatus);
        command.Parameters.AddWithValue("$lastSyncedAt", ToDbDate(record.LastSyncedAt));
        command.Parameters.AddWithValue("$errorMessage", string.Empty);
        command.Parameters.AddWithValue("$errorMessageCiphertext", fieldEncryptionService.Encrypt(record.ErrorMessage));
        command.Parameters.AddWithValue("$metadataJson", string.Empty);
        command.Parameters.AddWithValue("$metadataJsonCiphertext", fieldEncryptionService.Encrypt(record.MetadataJson));
        command.Parameters.AddWithValue("$createdAt", record.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", record.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$deletedAt", ToDbDate(record.DeletedAt));
        command.Parameters.AddWithValue("$isSynced", record.IsSynced ? 1 : 0);
        command.Parameters.AddWithValue("$version", record.Version);
    }

    private static SyncRecord Map(SqliteDataReader reader, IFieldEncryptionService fieldEncryptionService)
    {
        var errorMessage = EncryptedColumnReader.ReadRequiredString(reader, 7, fieldEncryptionService, "SyncRecords.ErrorMessageCiphertext");
        var metadataJson = EncryptedColumnReader.ReadRequiredString(reader, 9, fieldEncryptionService, "SyncRecords.MetadataJsonCiphertext");

        return new SyncRecord
        {
            Id = reader.GetInt32(0),
            EntityType = reader.GetString(1),
            EntityId = reader.GetInt32(2),
            RemoteId = reader.GetString(3),
            SyncStatus = (SyncStatus)reader.GetInt32(4),
            LastSyncedAt = reader.IsDBNull(5) ? null : DateTime.Parse(reader.GetString(5), null, DateTimeStyles.RoundtripKind),
            ErrorMessage = errorMessage,
            MetadataJson = metadataJson,
            CreatedAt = DateTime.Parse(reader.GetString(10), null, DateTimeStyles.RoundtripKind),
            UpdatedAt = DateTime.Parse(reader.GetString(11), null, DateTimeStyles.RoundtripKind),
            DeletedAt = reader.IsDBNull(12) ? null : DateTime.Parse(reader.GetString(12), null, DateTimeStyles.RoundtripKind),
            IsSynced = reader.GetInt32(13) == 1,
            Version = reader.GetInt32(14)
        };
    }

    private static object ToDbDate(DateTime? value)
    {
        return value is null ? DBNull.Value : value.Value.ToString("O");
    }
}
