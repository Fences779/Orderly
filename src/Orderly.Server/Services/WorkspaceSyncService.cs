using System.Data;
using Dapper;

namespace Orderly.Server.Services;

public sealed class WorkspaceSyncService : IWorkspaceSyncService
{
    public async Task<long> AllocateSequenceAsync(IDbConnection connection, IDbTransaction transaction, Guid workspaceId)
    {
        const string sql = @"
            INSERT INTO ""CloudWorkspaceSyncState"" (""WorkspaceId"", ""LastSequence"", ""UpdatedAt"")
            VALUES (@workspaceId, 1, @now)
            ON CONFLICT (""WorkspaceId"") DO UPDATE SET
                ""LastSequence"" = ""CloudWorkspaceSyncState"".""LastSequence"" + 1,
                ""UpdatedAt"" = @now
            RETURNING ""LastSequence"";";

        var sequence = await connection.ExecuteScalarAsync<long>(sql, new { workspaceId, now = DateTime.UtcNow }, transaction);
        return sequence;
    }

    public async Task RecordChangeAsync(IDbConnection connection, IDbTransaction transaction, Guid workspaceId, long sequence, string entityType, Guid? entityId, string action, long? revision, Guid? actorUserId, string? payloadHintJson)
    {
        const string sql = @"
            INSERT INTO ""CloudChangeLog"" (""WorkspaceId"", ""Sequence"", ""EntityType"", ""EntityId"", ""Action"", ""Revision"", ""ActorUserId"", ""OccurredAt"", ""PayloadHintJson"")
            VALUES (@workspaceId, @sequence, @entityType, @entityId, @action, @revision, @actorUserId, @now, @payloadHintJson);";

        await connection.ExecuteAsync(sql, new
        {
            workspaceId,
            sequence,
            entityType,
            entityId,
            action,
            revision,
            actorUserId,
            now = DateTime.UtcNow,
            payloadHintJson
        }, transaction);
    }
}
