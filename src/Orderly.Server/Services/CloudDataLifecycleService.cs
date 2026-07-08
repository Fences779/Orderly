using System.Data;
using System.Security.Cryptography;
using Dapper;
using Microsoft.AspNetCore.Http;
using Npgsql;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Permissions;
using Orderly.Core.Commerce;
using Orderly.Server.Data;
using Orderly.Server.Models;

namespace Orderly.Server.Services;

public sealed class CloudDataLifecycleService : ICloudDataLifecycleService
{
    private readonly PostgresConnectionFactory _connectionFactory;
    private readonly IBlobStorage _blobStorage;
    private readonly IAuditLogService _auditLog;
    private readonly IWorkspaceSyncService _syncService;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICloudAuthService _authService;
    private readonly ICloudPermissionService _permissions;
    private readonly ServerOptions _options;

    public CloudDataLifecycleService(
        PostgresConnectionFactory connectionFactory,
        IBlobStorage blobStorage,
        IAuditLogService auditLog,
        IWorkspaceSyncService syncService,
        ICurrentUserContext currentUser,
        ICloudAuthService authService,
        ICloudPermissionService permissions,
        ServerOptions options)
    {
        _connectionFactory = connectionFactory;
        _blobStorage = blobStorage;
        _auditLog = auditLog;
        _syncService = syncService;
        _currentUser = currentUser;
        _authService = authService;
        _permissions = permissions;
        _options = options;
    }

    public async Task<IReadOnlyList<CloudEntityVersionDto>> ListHistoryAsync(Guid workspaceId, string entityType, Guid entityId, CancellationToken cancellationToken = default)
    {
        var normalizedEntityType = ResolveEntityType(entityType);
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);
        if (!await CanReadEntityAsync(connection, workspaceId, normalizedEntityType, entityId, historyPayload: true))
        {
            throw new UnauthorizedAccessException("没有历史版本查看权限。");
        }

        var rows = await connection.QueryAsync<CloudEntityVersionDto>(
            @"SELECT v.""Id"", v.""WorkspaceId"", v.""EntityType"", v.""EntityId"", v.""Revision"", v.""Action"",
                     v.""PayloadJson""::text AS ""PayloadJson"", v.""CreatedByUserId"",
                     COALESCE(u.""DisplayName"", '') AS ""CreatedByDisplayName"",
                     v.""CreatedAt"" AS ""CreatedAtUtc""
              FROM ""CloudEntityVersions"" v
              LEFT JOIN ""CloudUsers"" u ON u.""Id"" = v.""CreatedByUserId""
              WHERE v.""WorkspaceId"" = @workspaceId AND v.""EntityType"" = @entityType AND v.""EntityId"" = @entityId
              ORDER BY v.""Revision"" DESC, v.""CreatedAt"" DESC;",
            new { workspaceId, entityType = normalizedEntityType, entityId });
        await _auditLog.LogAsync(workspaceId, "EntityHistoryViewed", normalizedEntityType, entityId, null, null, result: "Succeeded");
        return rows.ToList();
    }

    public async Task<IReadOnlyList<CloudAttachmentDto>> ListAttachmentsAsync(Guid workspaceId, string entityType, Guid entityId, CancellationToken cancellationToken = default)
    {
        var normalizedEntityType = ResolveEntityType(entityType);
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);
        if (!await CanReadEntityAsync(connection, workspaceId, normalizedEntityType, entityId, historyPayload: false))
        {
            throw new UnauthorizedAccessException("没有附件列表查看权限。");
        }

        var rows = await connection.QueryAsync<CloudAttachmentDto>(
            AttachmentSelectSql + @"
              WHERE ""WorkspaceId"" = @workspaceId AND ""EntityType"" = @entityType AND ""EntityId"" = @entityId
              ORDER BY ""CreatedAt"" DESC;",
            new { workspaceId, entityType = normalizedEntityType, entityId });
        await _auditLog.LogAsync(workspaceId, "AttachmentListViewed", normalizedEntityType, entityId, null, null, result: "Succeeded");
        return rows.ToList();
    }

    public async Task<CloudAttachmentDto> UploadAttachmentAsync(Guid workspaceId, string entityType, Guid entityId, IFormFile file, string? clientRequestId, CancellationToken cancellationToken = default)
    {
        if (!_blobStorage.IsEnabled)
            throw new InvalidOperationException("附件对象存储未配置。");
        if (file.Length <= 0)
            throw new InvalidOperationException("附件不能为空。");

        var normalizedEntityType = ResolveEntityType(entityType);
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");
        var quotaBytes = Math.Max(1, _options.AttachmentQuotaBytes);

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await EnsureEntityExistsAsync(connection, workspaceId, normalizedEntityType, entityId);
        if (!await CanWriteEntityAsync(connection, workspaceId, normalizedEntityType, entityId))
        {
            throw new UnauthorizedAccessException("没有附件上传权限。");
        }

        var activeBytes = await connection.ExecuteScalarAsync<long>(
            @"SELECT COALESCE(SUM(""SizeBytes""), 0) FROM ""CloudAttachments""
              WHERE ""WorkspaceId"" = @workspaceId AND ""ArchivedAt"" IS NULL;",
            new { workspaceId });
        if (activeBytes + file.Length > quotaBytes)
            throw new InvalidOperationException("附件容量已超过当前 Workspace 配额。");

        await using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        var sha256 = Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
        buffer.Position = 0;

        var attachmentId = Guid.NewGuid();
        var blobKey = $"attachments/{workspaceId:N}/{attachmentId:N}/{SanitizeFileName(file.FileName)}";
        await _blobStorage.UploadAsync(blobKey, buffer, cancellationToken);

        var now = DateTime.UtcNow;
        await connection.ExecuteAsync(
            @"INSERT INTO ""CloudAttachments"" (
                ""Id"", ""WorkspaceId"", ""EntityType"", ""EntityId"", ""FileName"", ""ContentType"",
                ""SizeBytes"", ""Sha256"", ""BlobKey"", ""Version"", ""CreatedByUserId"", ""CreatedAt"")
              VALUES (
                @attachmentId, @workspaceId, @entityType, @entityId, @fileName, @contentType,
                @sizeBytes, @sha256, @blobKey, 1, @userId, @now);",
            new
            {
                attachmentId,
                workspaceId,
                entityType = normalizedEntityType,
                entityId,
                fileName = file.FileName,
                contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                sizeBytes = file.Length,
                sha256,
                blobKey,
                userId,
                now
            });

        await _auditLog.LogAsync(workspaceId, "AttachmentUploaded", normalizedEntityType, entityId, null, null, reason: file.FileName, clientRequestId);
        return await GetAttachmentDtoAsync(connection, workspaceId, attachmentId)
            ?? throw new InvalidOperationException("附件上传后无法读取元数据。");
    }

    public async Task<(CloudAttachmentDto Attachment, Stream Content)> DownloadAttachmentAsync(Guid workspaceId, Guid attachmentId, CancellationToken cancellationToken = default)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var record = await connection.QueryFirstOrDefaultAsync<AttachmentRecord>(
            @"SELECT * FROM ""CloudAttachments""
              WHERE ""WorkspaceId"" = @workspaceId AND ""Id"" = @attachmentId AND ""ArchivedAt"" IS NULL;",
            new { workspaceId, attachmentId });
        if (record == null) throw new InvalidOperationException("附件不存在或已归档。");
        if (!await CanReadEntityAsync(connection, workspaceId, record.EntityType, record.EntityId, historyPayload: false))
        {
            throw new UnauthorizedAccessException("没有附件下载权限。");
        }

        var stream = await _blobStorage.DownloadAsync(record.BlobKey, cancellationToken)
            ?? throw new InvalidOperationException("附件文件不存在。");
        await _auditLog.LogAsync(workspaceId, "AttachmentDownloaded", record.EntityType, record.EntityId, null, null, reason: record.FileName);

        var attachment = await GetAttachmentDtoAsync(connection, workspaceId, attachmentId)
            ?? throw new InvalidOperationException("附件元数据不存在。");
        return (attachment, stream);
    }

    public async Task<bool> ArchiveAttachmentAsync(Guid workspaceId, Guid attachmentId, string? reason, string? clientRequestId, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var attachment = await GetAttachmentDtoAsync(connection, workspaceId, attachmentId);
        if (attachment == null || !await CanWriteEntityAsync(connection, workspaceId, attachment.EntityType, attachment.EntityId))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var affected = await connection.ExecuteAsync(
            @"UPDATE ""CloudAttachments""
              SET ""ArchivedByUserId"" = @userId, ""ArchivedAt"" = @now, ""ArchiveReason"" = @reason
              WHERE ""WorkspaceId"" = @workspaceId AND ""Id"" = @attachmentId AND ""ArchivedAt"" IS NULL;",
            new { userId, now, reason, workspaceId, attachmentId });
        if (affected == 0) return false;

        await _auditLog.LogAsync(workspaceId, "AttachmentArchived", attachment?.EntityType ?? "attachment", attachment?.EntityId, null, null, reason, clientRequestId);
        return true;
    }

    public async Task<bool> PermanentlyDeleteArchivedEntityAsync(Guid workspaceId, string entityType, Guid entityId, PermanentDeleteRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.Confirm) return false;

        var normalizedEntityType = ResolveEntityType(entityType);
        var tableName = ResolveEntityTable(normalizedEntityType);
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");
        var retentionCutoff = DateTime.UtcNow.AddDays(-Math.Max(1, _options.ArchiveRetentionDays));

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await ((NpgsqlConnection)connection).BeginTransactionAsync(cancellationToken);

        var row = await connection.QueryFirstOrDefaultAsync(
            $@"SELECT * FROM ""{tableName}""
               WHERE ""WorkspaceId"" = @workspaceId AND ""Id"" = @entityId AND ""Lifecycle"" = @archived
               FOR UPDATE;",
            new { workspaceId, entityId, archived = (int)EntityLifecycleStatus.Archived },
            transaction);
        if (row == null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        DateTime? deletedAt = row.DeletedAt;
        if (!deletedAt.HasValue || deletedAt.Value > retentionCutoff)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        long revision = (long)row.Revision;
        var sequence = await _syncService.AllocateSequenceAsync(connection, transaction, workspaceId);
        await RecordEntityVersionAsync(connection, transaction, workspaceId, normalizedEntityType, entityId, revision, "permanentlyDeleted", System.Text.Json.JsonSerializer.Serialize(row), userId);
        await connection.ExecuteAsync(
            $@"DELETE FROM ""{tableName}"" WHERE ""WorkspaceId"" = @workspaceId AND ""Id"" = @entityId;",
            new { workspaceId, entityId },
            transaction);
        await _auditLog.LogAsync(connection, transaction, workspaceId, "PermanentlyDeleted", normalizedEntityType, entityId, null, null, request.Reason, request.ClientRequestId);
        await _syncService.RecordChangeAsync(connection, transaction, workspaceId, sequence, normalizedEntityType, entityId, "permanentlyDeleted", null, userId, null);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public static async Task RecordEntityVersionAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid workspaceId,
        string entityType,
        Guid entityId,
        long revision,
        string action,
        string payloadJson,
        Guid? userId)
    {
        await connection.ExecuteAsync(
            @"INSERT INTO ""CloudEntityVersions"" (
                ""Id"", ""WorkspaceId"", ""EntityType"", ""EntityId"", ""Revision"", ""Action"",
                ""PayloadJson"", ""CreatedByUserId"", ""CreatedAt"")
              VALUES (
                @id, @workspaceId, @entityType, @entityId, @revision, @action,
                CAST(@payloadJson AS jsonb), @userId, @now)
              ON CONFLICT (""WorkspaceId"", ""EntityType"", ""EntityId"", ""Revision"", ""Action"") DO NOTHING;",
            new { id = Guid.NewGuid(), workspaceId, entityType, entityId, revision, action, payloadJson, userId, now = DateTime.UtcNow },
            transaction);
    }

    private async Task<CloudAttachmentDto?> GetAttachmentDtoAsync(IDbConnection connection, Guid workspaceId, Guid attachmentId)
    {
        return await connection.QueryFirstOrDefaultAsync<CloudAttachmentDto>(
            AttachmentSelectSql + @" WHERE ""WorkspaceId"" = @workspaceId AND ""Id"" = @attachmentId;",
            new { workspaceId, attachmentId });
    }

    private static async Task EnsureEntityExistsAsync(IDbConnection connection, Guid workspaceId, string entityType, Guid entityId)
    {
        var tableName = ResolveEntityTable(entityType);
        var exists = await connection.ExecuteScalarAsync<int>(
            $@"SELECT COUNT(*) FROM ""{tableName}""
               WHERE ""WorkspaceId"" = @workspaceId AND ""Id"" = @entityId AND ""DeletedAt"" IS NULL;",
            new { workspaceId, entityId });
        if (exists == 0) throw new InvalidOperationException("附件归属实体不存在。");
    }

    private async Task<bool> CanReadEntityAsync(IDbConnection connection, Guid workspaceId, string entityType, Guid entityId, bool historyPayload)
    {
        var membership = await GetMembershipAsync();
        if (membership == null || membership.WorkspaceId != workspaceId)
        {
            return false;
        }

        if (_permissions.IsAdmin(membership))
        {
            return true;
        }

        if (historyPayload && IsSensitiveHistoryEntity(entityType))
        {
            return false;
        }

        return await IsUserScopedEntityAsync(connection, workspaceId, entityType, entityId, membership.UserId);
    }

    private async Task<bool> CanWriteEntityAsync(IDbConnection connection, Guid workspaceId, string entityType, Guid entityId)
    {
        var membership = await GetMembershipAsync();
        if (membership == null || membership.WorkspaceId != workspaceId)
        {
            return false;
        }

        if (_permissions.IsAdmin(membership))
        {
            return true;
        }

        if (!_permissions.CanWriteBusinessData(membership))
        {
            return false;
        }

        return await IsUserScopedEntityAsync(connection, workspaceId, entityType, entityId, membership.UserId);
    }

    private async Task<CloudWorkspaceMemberRecord?> GetMembershipAsync()
    {
        var userId = _currentUser.UserId;
        return userId.HasValue ? await _authService.GetMembershipAsync(userId.Value) : null;
    }

    private static bool IsSensitiveHistoryEntity(string entityType)
        => entityType is EntityType.Order or EntityType.Product or EntityType.InventoryItem or EntityType.CashFlowEntry;

    private static async Task<bool> IsUserScopedEntityAsync(IDbConnection connection, Guid workspaceId, string entityType, Guid entityId, Guid userId)
    {
        var tableName = ResolveEntityTable(entityType);
        var hasAssignedTo = entityType is EntityType.Order or EntityType.Customer or EntityType.BusinessTask;
        var assignedSelect = hasAssignedTo ? @"""AssignedToUserId""" : "NULL::uuid";
        var row = await connection.QueryFirstOrDefaultAsync(
            $@"SELECT ""CreatedByUserId"", {assignedSelect} AS ""AssignedToUserId""
               FROM ""{tableName}""
               WHERE ""WorkspaceId"" = @workspaceId AND ""Id"" = @entityId AND ""DeletedAt"" IS NULL;",
            new { workspaceId, entityId });
        if (row == null)
        {
            return false;
        }

        Guid? createdBy = row.CreatedByUserId;
        Guid? assignedTo = row.AssignedToUserId;
        return createdBy == userId || assignedTo == userId;
    }

    private static string ResolveEntityType(string entityType)
        => entityType.ToLowerInvariant() switch
        {
            "order" or "orders" => EntityType.Order,
            "product" or "products" => EntityType.Product,
            "inventoryitem" or "inventory" => EntityType.InventoryItem,
            "customer" or "customers" => EntityType.Customer,
            "cashflow" or "cashflowentry" => EntityType.CashFlowEntry,
            "task" or "tasks" or "businesstask" => EntityType.BusinessTask,
            _ => throw new InvalidOperationException($"不支持的实体类型: {entityType}")
        };

    private static string ResolveEntityTable(string entityType)
        => entityType switch
        {
            EntityType.Order => "CommerceOrders",
            EntityType.Product => "CommerceProducts",
            EntityType.InventoryItem => "CommerceInventoryItems",
            EntityType.Customer => "CommerceCustomers",
            EntityType.CashFlowEntry => "CommerceCashFlowEntries",
            EntityType.BusinessTask => "CommerceBusinessTasks",
            _ => throw new InvalidOperationException($"不支持的实体类型: {entityType}")
        };

    private static string SanitizeFileName(string fileName)
    {
        var safe = Path.GetFileName(fileName);
        return string.IsNullOrWhiteSpace(safe) ? "attachment.bin" : safe;
    }

    private const string AttachmentSelectSql = @"
        SELECT ""Id"", ""WorkspaceId"", ""EntityType"", ""EntityId"", ""FileName"", ""ContentType"",
               ""SizeBytes"", ""Sha256"", ""Version"", ""CreatedAt"" AS ""CreatedAtUtc"",
               ""CreatedByUserId"", ""ArchivedAt"" AS ""ArchivedAtUtc"", ""ArchivedByUserId""
        FROM ""CloudAttachments""";

    private sealed class AttachmentRecord
    {
        public string EntityType { get; set; } = string.Empty;
        public Guid EntityId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string BlobKey { get; set; } = string.Empty;
    }
}
