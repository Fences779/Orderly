using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class LocalSyncService : ISyncService
{
    private readonly ISyncRecordRepository _syncRecordRepository;
    private readonly IActivityLogRepository _activityLogRepository;

    public LocalSyncService(ISyncRecordRepository syncRecordRepository, IActivityLogRepository activityLogRepository)
    {
        _syncRecordRepository = syncRecordRepository;
        _activityLogRepository = activityLogRepository;
    }

    public async Task<SyncRecord> MarkPendingAsync(string entityType, int entityId, CancellationToken cancellationToken = default)
    {
        var record = await GetOrCreateAsync(entityType, entityId, cancellationToken);
        record.SyncStatus = SyncStatus.Pending;
        record.ErrorMessage = string.Empty;
        return await SaveAsync(record, cancellationToken);
    }

    public async Task<SyncRecord> MarkSyncedAsync(string entityType, int entityId, string? remoteId = null, CancellationToken cancellationToken = default)
    {
        var record = await GetOrCreateAsync(entityType, entityId, cancellationToken);
        record.SyncStatus = SyncStatus.Synced;
        record.LastSyncedAt = DateTime.Now;
        record.ErrorMessage = string.Empty;
        if (!string.IsNullOrWhiteSpace(remoteId))
        {
            record.RemoteId = remoteId;
        }

        return await SaveAsync(record, cancellationToken);
    }

    public async Task<SyncRecord> MarkFailedAsync(string entityType, int entityId, string errorMessage, CancellationToken cancellationToken = default)
    {
        var record = await GetOrCreateAsync(entityType, entityId, cancellationToken);
        record.SyncStatus = SyncStatus.Failed;
        record.ErrorMessage = errorMessage;
        record.LastSyncedAt = null;
        var saved = await SaveAsync(record, cancellationToken);

        await _activityLogRepository.CreateAsync(new ActivityLog
        {
            Type = ActivityType.SyncFailed,
            Title = "同步失败",
            Description = $"{entityType}#{entityId}: {errorMessage}",
            Operator = "local-stub"
        }, cancellationToken);

        return saved;
    }

    public Task<IReadOnlyList<SyncRecord>> ListPendingAsync(CancellationToken cancellationToken = default)
    {
        return _syncRecordRepository.ListPendingAsync(cancellationToken);
    }

    private async Task<SyncRecord> GetOrCreateAsync(string entityType, int entityId, CancellationToken cancellationToken)
    {
        return await _syncRecordRepository.GetByEntityAsync(entityType, entityId, cancellationToken)
            ?? new SyncRecord
            {
                EntityType = entityType,
                EntityId = entityId,
                SyncStatus = SyncStatus.Pending
            };
    }

    private async Task<SyncRecord> SaveAsync(SyncRecord record, CancellationToken cancellationToken)
    {
        return record.Id <= 0
            ? await _syncRecordRepository.CreateAsync(record, cancellationToken)
            : await UpdateAsync(record, cancellationToken);
    }

    private async Task<SyncRecord> UpdateAsync(SyncRecord record, CancellationToken cancellationToken)
    {
        await _syncRecordRepository.UpdateAsync(record, cancellationToken);
        return record;
    }
}
