using Microsoft.Data.Sqlite;
using Orderly.Contracts.Offline;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Cloud;

public sealed class CloudOutboxStore : ICloudOutboxStore
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public CloudOutboxStore(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task AddAsync(CloudOutboxEntryDto entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var now = DateTime.UtcNow;
        if (entry.CreatedAtUtc == default) entry.CreatedAtUtc = now;
        entry.UpdatedAtUtc = now;

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO CloudOutboxEntries (
                Id, EntityType, EntityId, OperationType, PayloadJson, BaseRevision, ClientRequestId,
                Status, AttemptCount, NextAttemptAtUtc, LastSubmitError, CreatedAtUtc, UpdatedAtUtc)
            VALUES (
                $id, $entityType, $entityId, $operationType, $payloadJson, $baseRevision, $clientRequestId,
                $status, $attemptCount, $nextAttemptAtUtc, $lastSubmitError, $createdAtUtc, $updatedAtUtc)
            ON CONFLICT(ClientRequestId) DO UPDATE SET
                EntityType = excluded.EntityType,
                EntityId = excluded.EntityId,
                OperationType = excluded.OperationType,
                PayloadJson = excluded.PayloadJson,
                BaseRevision = excluded.BaseRevision,
                Status = excluded.Status,
                UpdatedAtUtc = excluded.UpdatedAtUtc;
            """;
        AddEntryParameters(command, entry);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<CloudOutboxEntryDto?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, EntityType, EntityId, OperationType, PayloadJson, BaseRevision, ClientRequestId,
                   Status, AttemptCount, NextAttemptAtUtc, LastSubmitError, CreatedAtUtc, UpdatedAtUtc
            FROM CloudOutboxEntries
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapEntry(reader) : null;
    }

    public async Task<IReadOnlyList<CloudOutboxEntryDto>> ListReadyAsync(DateTime utcNow, int limit = 100, CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 500);
        var entries = new List<CloudOutboxEntryDto>();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, EntityType, EntityId, OperationType, PayloadJson, BaseRevision, ClientRequestId,
                   Status, AttemptCount, NextAttemptAtUtc, LastSubmitError, CreatedAtUtc, UpdatedAtUtc
            FROM CloudOutboxEntries
            WHERE Status IN ($pending, $failed)
              AND (NextAttemptAtUtc IS NULL OR NextAttemptAtUtc <= $utcNow)
            ORDER BY CreatedAtUtc
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$pending", CloudOutboxStatus.Pending);
        command.Parameters.AddWithValue("$failed", CloudOutboxStatus.Failed);
        command.Parameters.AddWithValue("$utcNow", utcNow.ToString("O"));
        command.Parameters.AddWithValue("$limit", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(MapEntry(reader));
        }

        return entries;
    }

    public async Task MarkFailedAsync(string id, string error, DateTime nextAttemptAtUtc, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE CloudOutboxEntries
            SET Status = $status,
                AttemptCount = AttemptCount + 1,
                NextAttemptAtUtc = $nextAttemptAtUtc,
                LastSubmitError = $error,
                UpdatedAtUtc = $updatedAtUtc
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$status", CloudOutboxStatus.Failed);
        command.Parameters.AddWithValue("$nextAttemptAtUtc", nextAttemptAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$error", error);
        command.Parameters.AddWithValue("$updatedAtUtc", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MarkSubmittedAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE CloudOutboxEntries
            SET Status = $status,
                NextAttemptAtUtc = NULL,
                LastSubmitError = NULL,
                UpdatedAtUtc = $updatedAtUtc
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$status", CloudOutboxStatus.Submitted);
        command.Parameters.AddWithValue("$updatedAtUtc", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM CloudOutboxEntries WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddEntryParameters(SqliteCommand command, CloudOutboxEntryDto entry)
    {
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$entityType", entry.EntityType);
        command.Parameters.AddWithValue("$entityId", entry.EntityId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$operationType", entry.OperationType);
        command.Parameters.AddWithValue("$payloadJson", entry.PayloadJson);
        command.Parameters.AddWithValue("$baseRevision", entry.BaseRevision ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$clientRequestId", entry.ClientRequestId);
        command.Parameters.AddWithValue("$status", entry.Status);
        command.Parameters.AddWithValue("$attemptCount", entry.AttemptCount);
        command.Parameters.AddWithValue("$nextAttemptAtUtc", entry.NextAttemptAtUtc?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$lastSubmitError", entry.LastSubmitError ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$createdAtUtc", entry.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$updatedAtUtc", entry.UpdatedAtUtc.ToString("O"));
    }

    private static CloudOutboxEntryDto MapEntry(SqliteDataReader reader) => new()
    {
        Id = reader.GetString(0),
        EntityType = reader.GetString(1),
        EntityId = reader.IsDBNull(2) ? null : reader.GetString(2),
        OperationType = reader.GetString(3),
        PayloadJson = reader.GetString(4),
        BaseRevision = reader.IsDBNull(5) ? null : reader.GetInt64(5),
        ClientRequestId = reader.GetString(6),
        Status = reader.GetString(7),
        AttemptCount = reader.GetInt32(8),
        NextAttemptAtUtc = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
        LastSubmitError = reader.IsDBNull(10) ? null : reader.GetString(10),
        CreatedAtUtc = reader.GetDateTime(11),
        UpdatedAtUtc = reader.GetDateTime(12)
    };
}
