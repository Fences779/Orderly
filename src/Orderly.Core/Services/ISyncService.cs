using Orderly.Core.Models;

namespace Orderly.Core.Services;

public interface ISyncService
{
    Task<SyncRecord> MarkPendingAsync(string entityType, int entityId, CancellationToken cancellationToken = default);
    Task<SyncRecord> MarkSyncedAsync(string entityType, int entityId, string? remoteId = null, CancellationToken cancellationToken = default);
    Task<SyncRecord> MarkFailedAsync(string entityType, int entityId, string errorMessage, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SyncRecord>> ListPendingAsync(CancellationToken cancellationToken = default);
}
