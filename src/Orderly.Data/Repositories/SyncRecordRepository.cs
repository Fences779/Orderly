using Microsoft.Data.Sqlite;
using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;
using Orderly.Data.Services;
using Orderly.Data.Sqlite;
using System.Globalization;

namespace Orderly.Data.Repositories;

public sealed class SyncRecordRepository : ISyncRecordRepository
{
    private const int MaxEntityTypeCharacters = 80;
    private const int MaxRemoteIdCharacters = 160;
    private const int MaxErrorMessageCharacters = 2000;
    private const int MaxMetadataJsonCharacters = 8192;

    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly IFieldEncryptionService _fieldEncryptionService;

    public SyncRecordRepository(SqliteConnectionFactory connectionFactory, IFieldEncryptionService fieldEncryptionService)
    {
        _connectionFactory = connectionFactory;
        _fieldEncryptionService = fieldEncryptionService ?? throw new ArgumentNullException(nameof(fieldEncryptionService));
    }

    public async Task<SyncRecord> CreateAsync(SyncRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        NormalizeRecord(record);
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
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
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
        await UpdateEncryptedColumnsAsync(connection, transaction, record, _fieldEncryptionService, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetByIdAsync(record.Id, cancellationToken) ?? record;
    }

    public async Task UpdateAsync(SyncRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        NormalizeRecord(record);
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
        entityType = NormalizeRequiredText(entityType, MaxEntityTypeCharacters, "同步实体类型", allowLineBreaks: false);
        EnsureEntityId(entityId);

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
        entityType = NormalizeRequiredText(entityType, MaxEntityTypeCharacters, "同步实体类型", allowLineBreaks: false);

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

    public async Task<IReadOnlyList<SyncRecord>> ListFailedOrConflictedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                Id, EntityType, EntityId, RemoteId, SyncStatus, LastSyncedAt, ErrorMessage, ErrorMessageCiphertext, MetadataJson, MetadataJsonCiphertext,
                CreatedAt, UpdatedAt, DeletedAt, IsSynced, Version
            FROM SyncRecords
            WHERE DeletedAt IS NULL AND SyncStatus IN ($failedStatus, $conflictStatus)
            ORDER BY UpdatedAt DESC, Id DESC;
            """;
        command.Parameters.AddWithValue("$failedStatus", (int)SyncStatus.Failed);
        command.Parameters.AddWithValue("$conflictStatus", (int)SyncStatus.Conflict);

        var rows = new List<SyncRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(Map(reader, _fieldEncryptionService));
        }

        return rows;
    }

    private static void NormalizeRecord(SyncRecord record)
    {
        record.EntityType = NormalizeRequiredText(record.EntityType, MaxEntityTypeCharacters, "同步实体类型", allowLineBreaks: false);
        EnsureEntityId(record.EntityId);

        if (!Enum.IsDefined(record.SyncStatus))
        {
            throw new InvalidOperationException("同步状态无效。");
        }

        record.RemoteId = NormalizeOptionalText(record.RemoteId, MaxRemoteIdCharacters, "同步远端标识", allowLineBreaks: false);
        record.ErrorMessage = NormalizeOptionalText(record.ErrorMessage, MaxErrorMessageCharacters, "同步错误消息", allowLineBreaks: true);
        record.MetadataJson = NormalizeOptionalText(record.MetadataJson, MaxMetadataJsonCharacters, "同步元数据", allowLineBreaks: false);
    }

    private static void EnsureEntityId(int entityId)
    {
        if (entityId <= 0)
        {
            throw new InvalidOperationException("同步实体标识无效。");
        }
    }

    private static string NormalizeRequiredText(string? value, int maxCharacters, string fieldName, bool allowLineBreaks)
    {
        var normalized = NormalizeOptionalText(value, maxCharacters, fieldName, allowLineBreaks);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException($"{fieldName}不能为空。");
        }

        return normalized;
    }

    private static string NormalizeOptionalText(string? value, int maxCharacters, string fieldName, bool allowLineBreaks)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length > maxCharacters)
        {
            throw new InvalidOperationException($"{fieldName}不能超过 {maxCharacters} 个字符。");
        }

        if (normalized.Any(ch => char.IsControl(ch) && !(allowLineBreaks && ch is '\r' or '\n' or '\t')))
        {
            throw new InvalidOperationException($"{fieldName}不能包含控制字符。");
        }

        return normalized;
    }

    private static void AddParameters(SqliteCommand command, SyncRecord record, IFieldEncryptionService fieldEncryptionService)
    {
        command.Parameters.AddWithValue("$entityType", record.EntityType);
        command.Parameters.AddWithValue("$entityId", record.EntityId);
        command.Parameters.AddWithValue("$remoteId", record.RemoteId);
        command.Parameters.AddWithValue("$syncStatus", (int)record.SyncStatus);
        command.Parameters.AddWithValue("$lastSyncedAt", ToDbDate(record.LastSyncedAt));
        command.Parameters.AddWithValue("$errorMessage", string.Empty);
        command.Parameters.AddWithValue("$errorMessageCiphertext", EncryptedFieldScope.EncryptOrEmpty(fieldEncryptionService, record.ErrorMessage, "SyncRecords.ErrorMessageCiphertext", record.Id));
        command.Parameters.AddWithValue("$metadataJson", string.Empty);
        command.Parameters.AddWithValue("$metadataJsonCiphertext", EncryptedFieldScope.EncryptOrEmpty(fieldEncryptionService, record.MetadataJson, "SyncRecords.MetadataJsonCiphertext", record.Id));
        command.Parameters.AddWithValue("$createdAt", record.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", record.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$deletedAt", ToDbDate(record.DeletedAt));
        command.Parameters.AddWithValue("$isSynced", record.IsSynced ? 1 : 0);
        command.Parameters.AddWithValue("$version", record.Version);
    }

    private static async Task UpdateEncryptedColumnsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SyncRecord record,
        IFieldEncryptionService fieldEncryptionService,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE SyncRecords
            SET ErrorMessageCiphertext = $errorMessageCiphertext,
                MetadataJsonCiphertext = $metadataJsonCiphertext
            WHERE Id = $id;
            """;
        AddParameters(command, record, fieldEncryptionService);
        command.Parameters.AddWithValue("$id", record.Id);
        await command.ExecuteNonQueryAsync(cancellationToken);
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
