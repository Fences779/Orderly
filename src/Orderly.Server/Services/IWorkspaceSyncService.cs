using System.Data;

namespace Orderly.Server.Services;

public interface IWorkspaceSyncService
{
    Task<long> AllocateSequenceAsync(IDbConnection connection, IDbTransaction transaction, Guid workspaceId);
    Task RecordChangeAsync(IDbConnection connection, IDbTransaction transaction, Guid workspaceId, long sequence, string entityType, Guid? entityId, string action, long? revision, Guid? actorUserId, string? payloadHintJson);
}
