using Orderly.Core.Models;

namespace Orderly.Core.Repositories;

public interface ISyncRecordRepository
{
    Task<SyncRecord> CreateAsync(SyncRecord record, CancellationToken cancellationToken = default);
    Task UpdateAsync(SyncRecord record, CancellationToken cancellationToken = default);
    Task<SyncRecord?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<SyncRecord?> GetByEntityAsync(string entityType, int entityId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SyncRecord>> ListPendingAsync(CancellationToken cancellationToken = default);
}
