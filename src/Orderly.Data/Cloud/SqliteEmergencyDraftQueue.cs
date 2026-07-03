using Microsoft.Data.Sqlite;
using Orderly.Contracts.Offline;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Cloud;

/// <summary>
/// SQLCipher-backed implementation of <see cref="IEmergencyDraftQueue"/>.
/// Stores offline write attempts in the per-workspace encrypted database and surfaces them for
/// submission once the client regains connectivity.
/// </summary>
public sealed class SqliteEmergencyDraftQueue : IEmergencyDraftQueue
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteEmergencyDraftQueue(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task AddAsync(EmergencyDraftDto draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO EmergencyDrafts (Id, EntityType, EntityId, OperationType, PayloadJson, BaseRevision, CreatedAtUtc, Status, LastSubmitError)
            VALUES ($id, $entityType, $entityId, $operationType, $payloadJson, $baseRevision, $createdAtUtc, $status, $lastSubmitError)
            ON CONFLICT(Id) DO UPDATE SET
                EntityType = excluded.EntityType,
                EntityId = excluded.EntityId,
                OperationType = excluded.OperationType,
                PayloadJson = excluded.PayloadJson,
                BaseRevision = excluded.BaseRevision,
                CreatedAtUtc = excluded.CreatedAtUtc,
                Status = excluded.Status,
                LastSubmitError = excluded.LastSubmitError;
            """;
        command.Parameters.AddWithValue("$id", draft.Id);
        command.Parameters.AddWithValue("$entityType", draft.EntityType);
        command.Parameters.AddWithValue("$entityId", draft.EntityId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$operationType", draft.OperationType);
        command.Parameters.AddWithValue("$payloadJson", draft.PayloadJson);
        command.Parameters.AddWithValue("$baseRevision", draft.BaseRevision ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$createdAtUtc", draft.CreatedAtUtc.ToString("O"));
        command.Parameters.AddWithValue("$status", draft.Status);
        command.Parameters.AddWithValue("$lastSubmitError", draft.LastSubmitError ?? (object)DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EmergencyDraftDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var drafts = new List<EmergencyDraftDto>();

        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, EntityType, EntityId, OperationType, PayloadJson, BaseRevision, CreatedAtUtc, Status, LastSubmitError
            FROM EmergencyDrafts
            ORDER BY CreatedAtUtc;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            drafts.Add(MapDraft(reader));
        }

        return drafts;
    }

    public async Task<EmergencyDraftDto?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, EntityType, EntityId, OperationType, PayloadJson, BaseRevision, CreatedAtUtc, Status, LastSubmitError
            FROM EmergencyDrafts
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return MapDraft(reader);
    }

    public async Task RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM EmergencyDrafts WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateStatusAsync(string id, string status, string? error, CancellationToken cancellationToken = default)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE EmergencyDrafts
            SET Status = $status, LastSubmitError = $error
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$error", error ?? (object)DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static EmergencyDraftDto MapDraft(SqliteDataReader reader)
    {
        return new EmergencyDraftDto
        {
            Id = reader.GetString(0),
            EntityType = reader.GetString(1),
            EntityId = reader.IsDBNull(2) ? null : reader.GetString(2),
            OperationType = reader.GetString(3),
            PayloadJson = reader.GetString(4),
            BaseRevision = reader.IsDBNull(5) ? null : reader.GetInt64(5),
            CreatedAtUtc = reader.GetDateTime(6),
            Status = reader.GetString(7),
            LastSubmitError = reader.IsDBNull(8) ? null : reader.GetString(8)
        };
    }
}
