using Dapper;
using Orderly.Contracts.Offline;
using Orderly.Server.Data;
using Orderly.Server.Models;

namespace Orderly.Server.Services;

public interface IEmergencyDraftRepository
{
    Task AddAsync(CloudEmergencyDraftRecord draft, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CloudEmergencyDraftRecord>> ListPendingAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CloudEmergencyDraftRecord>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> ListWorkspaceIdsWithPendingAsync(CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(Guid id, string status, string? error, DateTime? submittedAt, CancellationToken cancellationToken = default);
    Task<CloudEmergencyDraftRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class EmergencyDraftRepository : IEmergencyDraftRepository
{
    private readonly PostgresConnectionFactory _connectionFactory;

    public EmergencyDraftRepository(PostgresConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task AddAsync(CloudEmergencyDraftRecord draft, CancellationToken cancellationToken = default)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);
        const string sql = @"
            INSERT INTO ""CloudEmergencyDrafts"" (
                ""Id"", ""WorkspaceId"", ""SubmittedByUserId"", ""EntityType"", ""EntityId"", ""OperationType"",
                ""PayloadJson"", ""BaseRevision"", ""Status"", ""LastSubmitError"", ""CreatedAt"", ""SubmittedAt"")
            VALUES (
                @Id, @WorkspaceId, @SubmittedByUserId, @EntityType, @EntityId, @OperationType,
                @PayloadJson, @BaseRevision, @Status, @LastSubmitError, @CreatedAt, @SubmittedAt)
            ON CONFLICT (""Id"") DO NOTHING;";

        await connection.ExecuteAsync(sql, draft);
    }

    public async Task<IReadOnlyList<CloudEmergencyDraftRecord>> ListPendingAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);
        const string sql = @"
            SELECT * FROM ""CloudEmergencyDrafts""
            WHERE ""WorkspaceId"" = @workspaceId AND ""Status"" = @pending
            ORDER BY ""CreatedAt"" ASC;";

        var drafts = await connection.QueryAsync<CloudEmergencyDraftRecord>(
            sql,
            new { workspaceId, pending = EmergencyDraftStatus.Pending });
        return drafts.ToList();
    }

    public async Task<IReadOnlyList<CloudEmergencyDraftRecord>> ListByWorkspaceAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);
        const string sql = @"
            SELECT * FROM ""CloudEmergencyDrafts""
            WHERE ""WorkspaceId"" = @workspaceId
            ORDER BY ""CreatedAt"" DESC;";

        var drafts = await connection.QueryAsync<CloudEmergencyDraftRecord>(sql, new { workspaceId });
        return drafts.ToList();
    }

    public async Task<IReadOnlyList<Guid>> ListWorkspaceIdsWithPendingAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);
        const string sql = @"
            SELECT DISTINCT ""WorkspaceId"" FROM ""CloudEmergencyDrafts""
            WHERE ""Status"" = @pending;";

        var ids = await connection.QueryAsync<Guid>(sql, new { pending = EmergencyDraftStatus.Pending });
        return ids.ToList();
    }

    public async Task UpdateStatusAsync(Guid id, string status, string? error, DateTime? submittedAt, CancellationToken cancellationToken = default)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);
        const string sql = @"
            UPDATE ""CloudEmergencyDrafts""
            SET ""Status"" = @status,
                ""LastSubmitError"" = @error,
                ""SubmittedAt"" = @submittedAt
            WHERE ""Id"" = @id;";

        await connection.ExecuteAsync(sql, new { id, status, error, submittedAt });
    }

    public async Task<CloudEmergencyDraftRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);
        const string sql = @"SELECT * FROM ""CloudEmergencyDrafts"" WHERE ""Id"" = @id;";
        return await connection.QueryFirstOrDefaultAsync<CloudEmergencyDraftRecord>(sql, new { id });
    }
}
