using Orderly.Core.Models;
using Orderly.Core.Repositories;
using Orderly.Core.Services;

namespace Orderly.Data.Services;

public sealed class LocalSyncService : ISyncService
{
    private const int MaxEntityTypeCharacters = 80;
    private const int MaxRemoteIdCharacters = 160;
    private const int MaxErrorMessageCharacters = 2000;
    private const int MaxMetadataJsonCharacters = 8192;
    private const int MaxActivityDescriptionCharacters = 2000;

    private readonly ISyncRecordRepository _syncRecordRepository;
    private readonly IActivityLogRepository _activityLogRepository;

    public LocalSyncService(ISyncRecordRepository syncRecordRepository, IActivityLogRepository activityLogRepository)
    {
        _syncRecordRepository = syncRecordRepository;
        _activityLogRepository = activityLogRepository;
    }

    public async Task<SyncRecord> MarkPendingAsync(string entityType, int entityId, string? metadataJson = null, CancellationToken cancellationToken = default)
    {
        entityType = NormalizeRequiredText(entityType, MaxEntityTypeCharacters, "同步实体类型", allowLineBreaks: false);
        EnsureEntityId(entityId);

        var record = await GetOrCreateAsync(entityType, entityId, cancellationToken);
        record.SyncStatus = SyncStatus.Pending;
        record.ErrorMessage = string.Empty;
        if (metadataJson is not null)
        {
            record.MetadataJson = NormalizeOptionalText(metadataJson, MaxMetadataJsonCharacters, "同步元数据", allowLineBreaks: false);
        }

        return await SaveAsync(record, cancellationToken);
    }

    public async Task<SyncRecord> MarkSyncedAsync(string entityType, int entityId, string? remoteId = null, string? metadataJson = null, CancellationToken cancellationToken = default)
    {
        entityType = NormalizeRequiredText(entityType, MaxEntityTypeCharacters, "同步实体类型", allowLineBreaks: false);
        EnsureEntityId(entityId);

        var record = await GetOrCreateAsync(entityType, entityId, cancellationToken);
        record.SyncStatus = SyncStatus.Synced;
        record.LastSyncedAt = DateTime.Now;
        record.ErrorMessage = string.Empty;
        if (!string.IsNullOrWhiteSpace(remoteId))
        {
            record.RemoteId = NormalizeOptionalText(remoteId, MaxRemoteIdCharacters, "同步远端标识", allowLineBreaks: false);
        }

        if (metadataJson is not null)
        {
            record.MetadataJson = NormalizeOptionalText(metadataJson, MaxMetadataJsonCharacters, "同步元数据", allowLineBreaks: false);
        }

        return await SaveAsync(record, cancellationToken);
    }

    public async Task<SyncRecord> MarkFailedAsync(string entityType, int entityId, string errorMessage, string? metadataJson = null, CancellationToken cancellationToken = default)
    {
        entityType = NormalizeRequiredText(entityType, MaxEntityTypeCharacters, "同步实体类型", allowLineBreaks: false);
        EnsureEntityId(entityId);
        errorMessage = NormalizeOptionalText(errorMessage, MaxErrorMessageCharacters, "同步错误消息", allowLineBreaks: true);

        var record = await GetOrCreateAsync(entityType, entityId, cancellationToken);
        record.SyncStatus = SyncStatus.Failed;
        record.ErrorMessage = errorMessage;
        record.LastSyncedAt = null;
        if (metadataJson is not null)
        {
            record.MetadataJson = NormalizeOptionalText(metadataJson, MaxMetadataJsonCharacters, "同步元数据", allowLineBreaks: false);
        }

        var saved = await SaveAsync(record, cancellationToken);

        await _activityLogRepository.CreateAsync(new ActivityLog
        {
            Type = ActivityType.SyncFailed,
            Title = "同步失败",
            Description = BuildSyncFailureDescription(saved.EntityType, saved.EntityId, errorMessage),
            Operator = "local-stub",
            MetadataJson = saved.MetadataJson
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

    private static string BuildSyncFailureDescription(string entityType, int entityId, string errorMessage)
    {
        var singleLineError = new string(errorMessage
            .Select(static ch => ch is '\r' or '\n' or '\t' ? ' ' : ch)
            .ToArray())
            .Trim();
        var description = $"{entityType}#{entityId}: {singleLineError}";

        return description.Length <= MaxActivityDescriptionCharacters
            ? description
            : $"{description[..MaxActivityDescriptionCharacters]}...";
    }
}
