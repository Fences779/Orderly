using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Npgsql;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Permissions;
using Orderly.Core.Commerce;
using Orderly.Server.Data;
using Orderly.Server.Models;

namespace Orderly.Server.Services;

public interface ICloudImportService
{
    Task<LocalImportDryRunResponse> DryRunAsync(Guid workspaceId, LocalImportDryRunRequest request, CancellationToken cancellationToken = default);
    Task<LocalImportCommitResponse> CommitAsync(Guid workspaceId, LocalImportCommitRequest request, CancellationToken cancellationToken = default);
    Task<LocalImportBatchStatusDto?> GetBatchStatusAsync(Guid workspaceId, Guid batchId, CancellationToken cancellationToken = default);
}

public sealed class CloudImportService : ICloudImportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly PostgresConnectionFactory _connectionFactory;
    private readonly ICurrentUserContext _currentUser;
    private readonly ICloudPermissionService _permissions;
    private readonly IWorkspaceSyncService _syncService;
    private readonly IAuditLogService _auditLog;
    private readonly IDatabaseBackupService _backupService;
    private readonly ServerOptions _options;

    public CloudImportService(
        PostgresConnectionFactory connectionFactory,
        ICurrentUserContext currentUser,
        ICloudPermissionService permissions,
        IWorkspaceSyncService syncService,
        IAuditLogService auditLog,
        IDatabaseBackupService backupService,
        ServerOptions options)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
        _permissions = permissions;
        _syncService = syncService;
        _auditLog = auditLog;
        _backupService = backupService;
        _options = options;
    }

    public async Task<LocalImportDryRunResponse> DryRunAsync(Guid workspaceId, LocalImportDryRunRequest request, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        await RequireAdminAsync(workspaceId, userId);

        var fingerprint = ComputeFingerprint(request);
        var batchId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var report = new ImportBatchReport { Fingerprint = fingerprint, Package = request.Package };

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await BeginTransactionAsync(connection, cancellationToken);

        await InsertBatchAsync(connection, transaction, batchId, workspaceId, request.SourceInstanceId, fingerprint, report, userId, now);
        var (resolved, existingMapped, issues) = await ResolveTargetsAsync(connection, transaction, workspaceId, request.SourceInstanceId, request.Package, batchId, persistEntityMap: false);

        await transaction.CommitAsync(cancellationToken);

        var counts = ComputeCounts(resolved, request.Package, existingMapped);
        return new LocalImportDryRunResponse
        {
            DryRunBatchId = batchId,
            SourceFingerprint = fingerprint,
            Counts = counts,
            Issues = issues,
            CanCommit = issues.Count == 0 && counts.NewRecords + counts.ExistingMapped > 0
        };
    }

    public async Task<LocalImportCommitResponse> CommitAsync(Guid workspaceId, LocalImportCommitRequest request, CancellationToken cancellationToken = default)
    {
        var userId = RequireUserId();
        await RequireAdminAsync(workspaceId, userId);

        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);

        var batch = await GetBatchAsync(connection, request.DryRunBatchId, cancellationToken);
        if (batch == null || batch.WorkspaceId != workspaceId)
            throw new InvalidOperationException("Dry-run 批次不存在。");
        if (batch.SourceInstanceId != request.SourceInstanceId)
            throw new InvalidOperationException("提交来源与 DryRun 批次不一致。");
        if (batch.SourceFingerprint != request.SourceFingerprint)
            throw new InvalidOperationException("本地数据自 DryRun 后已发生变化，请重新执行 DryRun。");
        if (batch.Status == "Committed" || (batch.Status == "Failed" && !string.IsNullOrWhiteSpace(batch.ResultJson)))
            return RestoreCommitResponse(batch);
        if (batch.Status != "DryRun")
            throw new InvalidOperationException($"批次状态为 {batch.Status}，不能提交。");

        var report = JsonSerializer.Deserialize<ImportBatchReport>(batch.SourceReportJson, JsonOptions)
            ?? throw new InvalidOperationException("Dry-run 报告损坏，无法提交。");

        try
        {
            await using (var validationTransaction = await BeginTransactionAsync(connection, cancellationToken))
            {
                var (_, _, validationIssues) = await ResolveTargetsAsync(connection, validationTransaction, workspaceId, request.SourceInstanceId, report.Package, batch.Id, persistEntityMap: false);
                if (validationIssues.Count > 0)
                {
                    await validationTransaction.RollbackAsync(cancellationToken);
                    var response = new LocalImportCommitResponse
                    {
                        BatchId = batch.Id,
                        Status = "Failed",
                        Imported = new LocalImportCounts(),
                        Failures = validationIssues
                    };

                    await using var failedTransaction = await BeginTransactionAsync(connection, cancellationToken);
                    await UpdateBatchStatusAsync(connection, failedTransaction, batch.Id, "Failed", "导入校验未通过。", SerializeCommitResult(response));
                    await failedTransaction.CommitAsync(cancellationToken);
                    await LogImportFailureAsync(workspaceId, batch.Id, request.SourceFingerprint, response.Failures);

                    return response;
                }
            }

            var preImportBackupPath = await CreatePreImportBackupAsync(batch.Id, cancellationToken);
            await using var transaction = await BeginTransactionAsync(connection, cancellationToken);

            var sequence = await _syncService.AllocateSequenceAsync(connection, transaction, workspaceId);
            var (resolved, existingMapped, issues) = await ResolveTargetsAsync(connection, transaction, workspaceId, request.SourceInstanceId, report.Package, batch.Id, persistEntityMap: true);
            if (issues.Count > 0)
            {
                throw new InvalidOperationException("导入校验未通过。");
            }

            await InsertEntitiesAsync(connection, transaction, workspaceId, resolved, report.Package, userId, sequence, batch.Id);
            var counts = ComputeCounts(resolved, report.Package, existingMapped);
            var committedResponse = new LocalImportCommitResponse
            {
                BatchId = batch.Id,
                Status = "Committed",
                Imported = counts,
                Failures = issues
            };

            await UpdateBatchStatusAsync(connection, transaction, batch.Id, "Committed", null, SerializeCommitResult(committedResponse));

            await transaction.CommitAsync(cancellationToken);

            await LogImportSuccessAsync(workspaceId, batch.Id, request.SourceFingerprint, counts, preImportBackupPath);

            return committedResponse;
        }
        catch (Exception ex)
        {
            var response = new LocalImportCommitResponse
            {
                BatchId = batch.Id,
                Status = "Failed",
                Imported = new LocalImportCounts(),
                Failures = new List<LocalImportIssue>
                {
                    new() { EntityType = "batch", SourceLocalEntityId = batch.Id.ToString("N"), Message = ex.Message }
                }
            };

            await using var transaction = await BeginTransactionAsync(connection, cancellationToken);
            await UpdateBatchStatusAsync(connection, transaction, batch.Id, "Failed", ex.Message, SerializeCommitResult(response));
            await transaction.CommitAsync(cancellationToken);
            await LogImportFailureAsync(workspaceId, batch.Id, request.SourceFingerprint, response.Failures);

            return response;
        }
    }

    public async Task<LocalImportBatchStatusDto?> GetBatchStatusAsync(Guid workspaceId, Guid batchId, CancellationToken cancellationToken = default)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var row = await connection.QueryFirstOrDefaultAsync(
            @"SELECT ""Id"", ""WorkspaceId"", ""SourceInstanceId"", ""SourceFingerprint"", ""Status"", ""ErrorMessage"", ""DryRunAt"", ""CommittedAt""
              FROM ""CloudImportBatches""
              WHERE ""Id"" = @batchId AND ""WorkspaceId"" = @workspaceId;",
            new { batchId, workspaceId });

        if (row == null) return null;
        return new LocalImportBatchStatusDto
        {
            Id = row.Id,
            WorkspaceId = row.WorkspaceId,
            SourceInstanceId = row.SourceInstanceId,
            SourceFingerprint = row.SourceFingerprint,
            Status = row.Status,
            ErrorMessage = row.ErrorMessage,
            DryRunAtUtc = row.DryRunAt,
            CommittedAtUtc = row.CommittedAt as DateTime?
        };
    }

    private Guid RequireUserId()
    {
        return _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");
    }

    private async Task RequireAdminAsync(Guid workspaceId, Guid userId)
    {
        await using var connection = (System.Data.Common.DbConnection)await _connectionFactory.OpenConnectionAsync();
        var membership = await connection.QueryFirstOrDefaultAsync<CloudWorkspaceMemberRecord>(
            "SELECT * FROM \"CloudWorkspaceMembers\" WHERE \"UserId\" = @userId;",
            new { userId });
        if (membership == null || !membership.IsEnabled || membership.WorkspaceId != workspaceId)
            throw new UnauthorizedAccessException("Workspace access denied.");
        if (!_permissions.IsAdmin(membership))
            throw new UnauthorizedAccessException("只有管理员可以导入本地数据。");
    }

    private static async Task<NpgsqlTransaction> BeginTransactionAsync(System.Data.Common.DbConnection connection, CancellationToken cancellationToken)
        => await ((NpgsqlConnection)connection).BeginTransactionAsync(cancellationToken);

    private static string ComputeFingerprint(LocalImportDryRunRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.SourceFingerprint))
            return request.SourceFingerprint;

        var json = JsonSerializer.Serialize(request.Package, JsonOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static async Task InsertBatchAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid batchId,
        Guid workspaceId,
        Guid sourceInstanceId,
        string fingerprint,
        ImportBatchReport report,
        Guid userId,
        DateTime now)
    {
        const string sql = @"
            INSERT INTO ""CloudImportBatches"" (
                ""Id"", ""WorkspaceId"", ""SourceInstanceId"", ""SourceFingerprint"", ""SourceReportJson"",
                ""ResultJson"", ""Status"", ""RequestedByUserId"", ""DryRunAt"", ""CommittedAt"", ""RolledBackAt"", ""ErrorMessage"")
            VALUES (
                @batchId, @workspaceId, @sourceInstanceId, @fingerprint, @reportJson,
                NULL, 'DryRun', @userId, @now, NULL, NULL, NULL);";

        await connection.ExecuteAsync(sql, new
        {
            batchId,
            workspaceId,
            sourceInstanceId,
            fingerprint,
            reportJson = JsonSerializer.Serialize(report, JsonOptions),
            userId,
            now
        }, transaction);
    }

    private static async Task<CloudImportBatchRecord?> GetBatchAsync(System.Data.Common.DbConnection connection, Guid batchId, CancellationToken cancellationToken)
    {
        return await connection.QueryFirstOrDefaultAsync<CloudImportBatchRecord>(
            "SELECT * FROM \"CloudImportBatches\" WHERE \"Id\" = @batchId;",
            new { batchId });
    }

    private static async Task UpdateBatchStatusAsync(IDbConnection connection, IDbTransaction transaction, Guid batchId, string status, string? error, string? resultJson = null)
    {
        const string sql = @"
            UPDATE ""CloudImportBatches""
            SET ""Status"" = @status,
                ""ErrorMessage"" = @error,
                ""ResultJson"" = CASE WHEN @resultJson IS NULL THEN ""ResultJson"" ELSE @resultJson END,
                ""CommittedAt"" = CASE WHEN @status = 'Committed' THEN @now ELSE ""CommittedAt"" END
            WHERE ""Id"" = @batchId;";

        await connection.ExecuteAsync(sql, new { batchId, status, error, resultJson, now = DateTime.UtcNow }, transaction);
    }

    private static LocalImportCommitResponse RestoreCommitResponse(CloudImportBatchRecord batch)
    {
        if (!string.IsNullOrWhiteSpace(batch.ResultJson))
        {
            var saved = JsonSerializer.Deserialize<LocalImportCommitResponse>(batch.ResultJson, JsonOptions);
            if (saved != null)
            {
                return saved;
            }
        }

        return new LocalImportCommitResponse
        {
            BatchId = batch.Id,
            Status = batch.Status,
            Imported = new LocalImportCounts(),
            Failures = new List<LocalImportIssue>()
        };
    }

    private static string SerializeCommitResult(LocalImportCommitResponse response)
        => JsonSerializer.Serialize(response, JsonOptions);

    private async Task<string?> CreatePreImportBackupAsync(Guid batchId, CancellationToken cancellationToken)
    {
        if (!_options.RequirePreImportBackup)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var fileName = $"orderly_pre_import_{now:yyyyMMddHHmmss}_{batchId:N}.dump";
        var outputPath = Path.Combine(_options.LocalBackupDirectory, fileName);
        var backupPath = await _backupService.BackupAsync(outputPath, cancellationToken);
        var file = new FileInfo(backupPath);
        if (!file.Exists || file.Length == 0)
        {
            throw new InvalidOperationException("导入前备份未生成有效文件，已停止导入。");
        }

        BackupHealthState.Update(_options, snapshot =>
        {
            snapshot.LastPreImportBackupAtUtc = DateTime.UtcNow;
            snapshot.PreImportBackupPath = backupPath;
            snapshot.LastError = null;
        });

        return backupPath;
    }

    private async Task LogImportFailureAsync(Guid workspaceId, Guid batchId, string sourceFingerprint, List<LocalImportIssue> failures)
    {
        try
        {
            await _auditLog.LogAsync(
                workspaceId,
                "LocalDataImportFailed",
                EntityType.Order,
                batchId,
                null,
                JsonSerializer.Serialize(new { failures }, JsonOptions),
                clientRequestId: sourceFingerprint);
        }
        catch
        {
            // Keep the original import failure visible even when audit logging fails.
        }
    }

    private async Task LogImportSuccessAsync(Guid workspaceId, Guid batchId, string sourceFingerprint, LocalImportCounts counts, string? preImportBackupPath)
    {
        try
        {
            await _auditLog.LogAsync(
                workspaceId,
                "LocalDataImported",
                EntityType.Order,
                batchId,
                null,
                JsonSerializer.Serialize(new { imported = counts, preImportBackupPath }, JsonOptions),
                clientRequestId: sourceFingerprint);
        }
        catch
        {
            // The import was already committed; audit failure must not change batch status.
        }
    }

    private async Task<(ResolvedImport Resolved, int ExistingMapped, List<LocalImportIssue> Issues)> ResolveTargetsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid workspaceId,
        Guid sourceInstanceId,
        LocalImportPackage package,
        Guid batchId,
        bool persistEntityMap)
    {
        var resolved = new ResolvedImport();
        var issues = new List<LocalImportIssue>();
        var existingMap = await LoadExistingMapAsync(connection, transaction, workspaceId, sourceInstanceId);
        int existingMapped = 0;

        foreach (var dto in package.Products)
        {
            if (string.IsNullOrWhiteSpace(dto.SourceLocalEntityId))
            {
                issues.Add(new LocalImportIssue { EntityType = EntityType.Product, Message = "缺少 SourceLocalEntityId。" });
                continue;
            }

            var (targetId, isExisting) = await ResolveByMapOrStableKeyAsync(
                connection, transaction, workspaceId, sourceInstanceId, EntityType.Product, dto.SourceLocalEntityId,
                existingMap,
                async () =>
                {
                    if (string.IsNullOrWhiteSpace(dto.Code)) return null;
                    return await connection.QueryFirstOrDefaultAsync<Guid?>(
                        @"SELECT ""Id"" FROM ""CommerceProducts"" WHERE ""WorkspaceId"" = @workspaceId AND ""Code"" = @code AND ""DeletedAt"" IS NULL AND ""Lifecycle"" = 0;",
                        new { workspaceId, code = dto.Code.Trim() }, transaction);
                },
                batchId,
                persistEntityMap);

            resolved.Products[dto.SourceLocalEntityId] = targetId;
            if (isExisting) existingMapped++;
        }

        foreach (var dto in package.Customers)
        {
            if (string.IsNullOrWhiteSpace(dto.SourceLocalEntityId))
            {
                issues.Add(new LocalImportIssue { EntityType = EntityType.Customer, Message = "缺少 SourceLocalEntityId。" });
                continue;
            }

            var (targetId, isExisting) = await ResolveByMapOrStableKeyAsync(
                connection, transaction, workspaceId, sourceInstanceId, EntityType.Customer, dto.SourceLocalEntityId,
                existingMap,
                async () =>
                {
                    if (string.IsNullOrWhiteSpace(dto.Phone)) return null;
                    return await connection.QueryFirstOrDefaultAsync<Guid?>(
                        @"SELECT ""Id"" FROM ""CommerceCustomers"" WHERE ""WorkspaceId"" = @workspaceId AND ""Phone"" = @phone AND ""DeletedAt"" IS NULL AND ""Lifecycle"" = 0;",
                        new { workspaceId, phone = dto.Phone.Trim() }, transaction);
                },
                batchId,
                persistEntityMap);

            resolved.Customers[dto.SourceLocalEntityId] = targetId;
            if (isExisting) existingMapped++;
        }

        foreach (var dto in package.InventoryItems)
        {
            if (string.IsNullOrWhiteSpace(dto.SourceLocalEntityId))
            {
                issues.Add(new LocalImportIssue { EntityType = EntityType.InventoryItem, Message = "缺少 SourceLocalEntityId。" });
                continue;
            }

            var (targetId, isExisting) = await ResolveByMapOrStableKeyAsync(
                connection, transaction, workspaceId, sourceInstanceId, EntityType.InventoryItem, dto.SourceLocalEntityId,
                existingMap,
                async () =>
                {
                    if (string.IsNullOrWhiteSpace(dto.Sku)) return null;
                    return await connection.QueryFirstOrDefaultAsync<Guid?>(
                        @"SELECT ""Id"" FROM ""CommerceInventoryItems"" WHERE ""WorkspaceId"" = @workspaceId AND ""Sku"" = @sku AND ""DeletedAt"" IS NULL AND ""Lifecycle"" = 0;",
                        new { workspaceId, sku = dto.Sku.Trim() }, transaction);
                },
                batchId,
                persistEntityMap);

            resolved.InventoryItems[dto.SourceLocalEntityId] = targetId;
            if (isExisting) existingMapped++;
        }

        foreach (var dto in package.Orders)
        {
            if (string.IsNullOrWhiteSpace(dto.SourceLocalEntityId))
            {
                issues.Add(new LocalImportIssue { EntityType = EntityType.Order, Message = "缺少 SourceLocalEntityId。" });
                continue;
            }

            var (targetId, isExisting) = await ResolveByMapOrStableKeyAsync(
                connection, transaction, workspaceId, sourceInstanceId, EntityType.Order, dto.SourceLocalEntityId,
                existingMap,
                async () =>
                {
                    if (string.IsNullOrWhiteSpace(dto.OrderNo)) return null;
                    return await connection.QueryFirstOrDefaultAsync<Guid?>(
                        @"SELECT ""Id"" FROM ""CommerceOrders"" WHERE ""WorkspaceId"" = @workspaceId AND ""OrderNo"" = @orderNo AND ""DeletedAt"" IS NULL AND ""Lifecycle"" = 0;",
                        new { workspaceId, orderNo = dto.OrderNo.Trim() }, transaction);
                },
                batchId,
                persistEntityMap);

            resolved.Orders[dto.SourceLocalEntityId] = targetId;
            if (isExisting) existingMapped++;
        }

        foreach (var dto in package.OrderItems)
        {
            if (string.IsNullOrWhiteSpace(dto.SourceLocalEntityId))
            {
                issues.Add(new LocalImportIssue { EntityType = "orderItem", Message = "缺少 SourceLocalEntityId。" });
                continue;
            }

            var (targetId, isExisting) = await ResolveByMapOrStableKeyAsync(
                connection, transaction, workspaceId, sourceInstanceId, "orderItem", dto.SourceLocalEntityId,
                existingMap,
                () => Task.FromResult<Guid?>(null),
                batchId,
                persistEntityMap);

            resolved.OrderItems[dto.SourceLocalEntityId] = targetId;
            if (isExisting) existingMapped++;
        }

        foreach (var dto in package.PaymentRecords)
        {
            if (string.IsNullOrWhiteSpace(dto.SourceLocalEntityId))
            {
                issues.Add(new LocalImportIssue { EntityType = "paymentRecord", Message = "缺少 SourceLocalEntityId。" });
                continue;
            }

            var (targetId, isExisting) = await ResolveByMapOrStableKeyAsync(
                connection, transaction, workspaceId, sourceInstanceId, "paymentRecord", dto.SourceLocalEntityId,
                existingMap,
                () => Task.FromResult<Guid?>(null),
                batchId,
                persistEntityMap);

            resolved.PaymentRecords[dto.SourceLocalEntityId] = targetId;
            if (isExisting) existingMapped++;
        }

        foreach (var dto in package.CashFlowEntries)
        {
            if (string.IsNullOrWhiteSpace(dto.SourceLocalEntityId))
            {
                issues.Add(new LocalImportIssue { EntityType = EntityType.CashFlowEntry, Message = "缺少 SourceLocalEntityId。" });
                continue;
            }

            var (targetId, isExisting) = await ResolveByMapOrStableKeyAsync(
                connection, transaction, workspaceId, sourceInstanceId, EntityType.CashFlowEntry, dto.SourceLocalEntityId,
                existingMap,
                async () =>
                {
                    if (string.IsNullOrWhiteSpace(dto.BusinessKey)) return null;
                    return await connection.QueryFirstOrDefaultAsync<Guid?>(
                        @"SELECT ""Id"" FROM ""CommerceCashFlowEntries"" WHERE ""WorkspaceId"" = @workspaceId AND ""BusinessKey"" = @businessKey AND ""DeletedAt"" IS NULL AND ""Lifecycle"" = 0;",
                        new { workspaceId, businessKey = dto.BusinessKey.Trim() }, transaction);
                },
                batchId,
                persistEntityMap);

            resolved.CashFlowEntries[dto.SourceLocalEntityId] = targetId;
            if (isExisting) existingMapped++;
        }

        ValidateReferences(package, resolved, issues);
        return (resolved, existingMapped, issues);
    }

    private static async Task<Dictionary<string, Guid>> LoadExistingMapAsync(IDbConnection connection, IDbTransaction transaction, Guid workspaceId, Guid sourceInstanceId)
    {
        const string sql = @"
            SELECT ""EntityType"", ""SourceLocalEntityId"", ""TargetEntityId""
            FROM ""CloudImportEntityMap""
            WHERE ""WorkspaceId"" = @workspaceId AND ""SourceInstanceId"" = @sourceInstanceId;";

        var rows = await connection.QueryAsync(sql, new { workspaceId, sourceInstanceId }, transaction);
        return rows.ToDictionary(r => $"{r.EntityType}:{r.SourceLocalEntityId}", r => (Guid)r.TargetEntityId);
    }

    private static async Task<(Guid TargetId, bool IsExisting)> ResolveByMapOrStableKeyAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid workspaceId,
        Guid sourceInstanceId,
        string entityType,
        string sourceLocalEntityId,
        Dictionary<string, Guid> existingMap,
        Func<Task<Guid?>> findByStableKey,
        Guid batchId,
        bool persistEntityMap)
    {
        var key = $"{entityType}:{sourceLocalEntityId}";
        if (existingMap.TryGetValue(key, out var mappedId))
            return (mappedId, true);

        var stableId = await findByStableKey();
        if (stableId.HasValue)
        {
            if (persistEntityMap)
            {
                await UpsertMapAsync(connection, transaction, workspaceId, sourceInstanceId, entityType, sourceLocalEntityId, stableId.Value, batchId);
            }

            existingMap[key] = stableId.Value;
            return (stableId.Value, true);
        }

        var newId = Guid.NewGuid();
        if (persistEntityMap)
        {
            await UpsertMapAsync(connection, transaction, workspaceId, sourceInstanceId, entityType, sourceLocalEntityId, newId, batchId);
        }

        existingMap[key] = newId;
        return (newId, false);
    }

    private static void ValidateReferences(LocalImportPackage package, ResolvedImport resolved, List<LocalImportIssue> issues)
    {
        foreach (var dto in package.Orders)
        {
            if (!string.IsNullOrWhiteSpace(dto.SourceCustomerLocalEntityId)
                && !resolved.Customers.ContainsKey(dto.SourceCustomerLocalEntityId))
            {
                issues.Add(new LocalImportIssue
                {
                    EntityType = EntityType.Order,
                    SourceLocalEntityId = dto.SourceLocalEntityId,
                    Message = "订单引用的客户不存在。"
                });
            }
        }

        foreach (var dto in package.OrderItems)
        {
            if (string.IsNullOrWhiteSpace(dto.SourceOrderLocalEntityId)
                || !resolved.Orders.ContainsKey(dto.SourceOrderLocalEntityId))
            {
                issues.Add(new LocalImportIssue
                {
                    EntityType = "orderItem",
                    SourceLocalEntityId = dto.SourceLocalEntityId,
                    Message = "订单明细引用的订单不存在。"
                });
            }

            if (!string.IsNullOrWhiteSpace(dto.SourceProductLocalEntityId)
                && !resolved.Products.ContainsKey(dto.SourceProductLocalEntityId))
            {
                issues.Add(new LocalImportIssue
                {
                    EntityType = "orderItem",
                    SourceLocalEntityId = dto.SourceLocalEntityId,
                    Message = "订单明细引用的商品不存在。"
                });
            }
        }

        foreach (var dto in package.CashFlowEntries)
        {
            if (!string.IsNullOrWhiteSpace(dto.SourceOrderLocalEntityId)
                && !resolved.Orders.ContainsKey(dto.SourceOrderLocalEntityId))
            {
                issues.Add(new LocalImportIssue
                {
                    EntityType = EntityType.CashFlowEntry,
                    SourceLocalEntityId = dto.SourceLocalEntityId,
                    Message = "现金流引用的订单不存在。"
                });
            }
        }

        foreach (var dto in package.PaymentRecords)
        {
            if (!string.IsNullOrWhiteSpace(dto.SourceOrderLocalEntityId)
                && !resolved.Orders.ContainsKey(dto.SourceOrderLocalEntityId))
            {
                issues.Add(new LocalImportIssue
                {
                    EntityType = "paymentRecord",
                    SourceLocalEntityId = dto.SourceLocalEntityId,
                    Message = "付款记录引用的订单不存在。"
                });
            }

            if (!string.IsNullOrWhiteSpace(dto.SourceCashFlowEntryLocalEntityId)
                && !resolved.CashFlowEntries.ContainsKey(dto.SourceCashFlowEntryLocalEntityId))
            {
                issues.Add(new LocalImportIssue
                {
                    EntityType = "paymentRecord",
                    SourceLocalEntityId = dto.SourceLocalEntityId,
                    Message = "付款记录引用的现金流不存在。"
                });
            }
        }
    }

    private static async Task UpsertMapAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid workspaceId,
        Guid sourceInstanceId,
        string entityType,
        string sourceLocalEntityId,
        Guid targetEntityId,
        Guid batchId)
    {
        const string sql = @"
            INSERT INTO ""CloudImportEntityMap"" (
                ""WorkspaceId"", ""SourceInstanceId"", ""EntityType"", ""SourceLocalEntityId"",
                ""TargetEntityId"", ""FirstImportBatchId"", ""LastImportBatchId"", ""CreatedAt"", ""UpdatedAt"")
            VALUES (
                @workspaceId, @sourceInstanceId, @entityType, @sourceLocalEntityId,
                @targetEntityId, @batchId, @batchId, @now, @now)
            ON CONFLICT (""WorkspaceId"", ""SourceInstanceId"", ""EntityType"", ""SourceLocalEntityId"") DO UPDATE
            SET ""TargetEntityId"" = EXCLUDED.""TargetEntityId"",
                ""LastImportBatchId"" = EXCLUDED.""LastImportBatchId"",
                ""UpdatedAt"" = EXCLUDED.""UpdatedAt"";";

        await connection.ExecuteAsync(sql, new
        {
            workspaceId,
            sourceInstanceId,
            entityType,
            sourceLocalEntityId,
            targetEntityId,
            batchId,
            now = DateTime.UtcNow
        }, transaction);
    }

    private static LocalImportCounts ComputeCounts(ResolvedImport resolved, LocalImportPackage package, int existingMapped)
    {
        var total = resolved.Products.Count + resolved.Customers.Count + resolved.InventoryItems.Count +
                    resolved.Orders.Count + resolved.OrderItems.Count + resolved.PaymentRecords.Count + resolved.CashFlowEntries.Count;

        return new LocalImportCounts
        {
            Products = package.Products.Count,
            Customers = package.Customers.Count,
            InventoryItems = package.InventoryItems.Count,
            Orders = package.Orders.Count,
            OrderItems = package.OrderItems.Count,
            PaymentRecords = package.PaymentRecords.Count,
            CashFlowEntries = package.CashFlowEntries.Count,
            ExistingMapped = existingMapped,
            NewRecords = total - existingMapped
        };
    }

    private async Task InsertEntitiesAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid workspaceId,
        ResolvedImport resolved,
        LocalImportPackage package,
        Guid userId,
        long sequence,
        Guid batchId)
    {
        foreach (var kvp in resolved.Products)
        {
            if (await ExistsAsync(connection, transaction, "CommerceProducts", kvp.Value)) continue;
            var dto = package.Products.First(p => p.SourceLocalEntityId == kvp.Key);
            await InsertProductAsync(connection, transaction, workspaceId, kvp.Value, dto, userId, sequence);
            await RecordChangeAsync(connection, transaction, workspaceId, sequence, EntityType.Product, kvp.Value, "imported", 1L);
        }

        foreach (var kvp in resolved.Customers)
        {
            if (await ExistsAsync(connection, transaction, "CommerceCustomers", kvp.Value)) continue;
            var dto = package.Customers.First(p => p.SourceLocalEntityId == kvp.Key);
            await InsertCustomerAsync(connection, transaction, workspaceId, kvp.Value, dto, userId, sequence);
            await RecordChangeAsync(connection, transaction, workspaceId, sequence, EntityType.Customer, kvp.Value, "imported", 1L);
        }

        foreach (var kvp in resolved.InventoryItems)
        {
            if (await ExistsAsync(connection, transaction, "CommerceInventoryItems", kvp.Value)) continue;
            var dto = package.InventoryItems.First(p => p.SourceLocalEntityId == kvp.Key);
            var productId = ResolveReference(dto.SourceProductLocalEntityId, dto.ProductId, resolved.Products);
            await InsertInventoryItemAsync(connection, transaction, workspaceId, kvp.Value, dto, productId, userId, sequence);
            await RecordChangeAsync(connection, transaction, workspaceId, sequence, EntityType.InventoryItem, kvp.Value, "imported", 1L);
        }

        foreach (var kvp in resolved.Orders)
        {
            if (await ExistsAsync(connection, transaction, "CommerceOrders", kvp.Value)) continue;
            var dto = package.Orders.First(p => p.SourceLocalEntityId == kvp.Key);
            var customerId = ResolveReference(dto.SourceCustomerLocalEntityId, dto.CustomerId, resolved.Customers);
            await InsertOrderAsync(connection, transaction, workspaceId, kvp.Value, dto, customerId, userId, sequence);
            await RecordChangeAsync(connection, transaction, workspaceId, sequence, EntityType.Order, kvp.Value, "imported", 1L);
        }

        foreach (var kvp in resolved.OrderItems)
        {
            if (await ExistsAsync(connection, transaction, "CommerceOrderItems", kvp.Value)) continue;
            var dto = package.OrderItems.First(p => p.SourceLocalEntityId == kvp.Key);
            var orderId = resolved.Orders[dto.SourceOrderLocalEntityId];
            var productId = ResolveReference(dto.SourceProductLocalEntityId, dto.ProductId, resolved.Products);
            var inventoryItemId = ResolveReference(dto.SourceInventoryItemLocalEntityId, dto.InventoryItemId, resolved.InventoryItems);
            await InsertOrderItemAsync(connection, transaction, workspaceId, kvp.Value, dto, orderId, productId, inventoryItemId, userId, sequence);
        }

        foreach (var kvp in resolved.CashFlowEntries)
        {
            if (await ExistsAsync(connection, transaction, "CommerceCashFlowEntries", kvp.Value)) continue;
            var dto = package.CashFlowEntries.First(p => p.SourceLocalEntityId == kvp.Key);
            var orderId = ResolveReference(dto.SourceOrderLocalEntityId, dto.OrderId, resolved.Orders);
            await InsertCashFlowEntryAsync(connection, transaction, workspaceId, kvp.Value, dto, orderId, userId, sequence, batchId);
            await RecordChangeAsync(connection, transaction, workspaceId, sequence, EntityType.CashFlowEntry, kvp.Value, "imported", 1L);
        }

        foreach (var kvp in resolved.PaymentRecords)
        {
            if (await ExistsAsync(connection, transaction, "CommercePaymentRecords", kvp.Value)) continue;
            var dto = package.PaymentRecords.First(p => p.SourceLocalEntityId == kvp.Key);
            var orderId = ResolveReference(dto.SourceOrderLocalEntityId, dto.OrderId, resolved.Orders);
            var cashFlowEntryId = ResolveReference(dto.SourceCashFlowEntryLocalEntityId, dto.CashFlowEntryId, resolved.CashFlowEntries);
            await InsertPaymentRecordAsync(connection, transaction, workspaceId, kvp.Value, dto, orderId, cashFlowEntryId, userId, sequence);
        }
    }

    private static async Task InsertProductAsync(IDbConnection connection, IDbTransaction transaction, Guid workspaceId, Guid id, LocalProductDto dto, Guid userId, long sequence)
    {
        const string sql = @"
            INSERT INTO ""CommerceProducts"" (
                ""Id"", ""WorkspaceId"", ""CreatedAt"", ""UpdatedAt"", ""DeletedAt"", ""Lifecycle"",
                ""CustomFieldsJson"", ""Revision"", ""CreatedByUserId"", ""UpdatedByUserId"", ""LastChangeSequence"",
                ""Name"", ""Code"", ""ProductType"", ""Description"", ""DefaultUnitId"", ""SupplierId"", ""DefaultPrice"", ""DefaultCost"")
            VALUES (
                @id, @workspaceId, @createdAt, @updatedAt, NULL, 0,
                NULL, 1, @createdBy, @updatedBy, @sequence,
                @name, @code, @productType, @description, @defaultUnitId, @supplierId, @defaultPrice, @defaultCost);";

        await connection.ExecuteAsync(sql, new
        {
            id,
            workspaceId,
            createdAt = dto.CreatedAtUtc,
            updatedAt = dto.UpdatedAtUtc,
            createdBy = userId,
            updatedBy = userId,
            sequence,
            name = dto.Name.Trim(),
            code = (dto.Code ?? string.Empty).Trim(),
            productType = (int)dto.ProductType,
            description = dto.Description,
            defaultUnitId = dto.DefaultUnitId,
            supplierId = dto.SupplierId,
            defaultPrice = RoundMoney(dto.DefaultPrice),
            defaultCost = dto.DefaultCost.HasValue ? RoundMoney(dto.DefaultCost.Value) : (decimal?)null
        }, transaction);
    }

    private static async Task InsertCustomerAsync(IDbConnection connection, IDbTransaction transaction, Guid workspaceId, Guid id, LocalCustomerDto dto, Guid userId, long sequence)
    {
        const string sql = @"
            INSERT INTO ""CommerceCustomers"" (
                ""Id"", ""WorkspaceId"", ""CreatedAt"", ""UpdatedAt"", ""DeletedAt"", ""Lifecycle"",
                ""CustomFieldsJson"", ""Revision"", ""CreatedByUserId"", ""UpdatedByUserId"", ""LastChangeSequence"",
                ""Name"", ""Phone"", ""WeChat"", ""Email"", ""LastOrderAt"", ""CompletedOrderCount"", ""TotalSpend"")
            VALUES (
                @id, @workspaceId, @createdAt, @updatedAt, NULL, 0,
                NULL, 1, @createdBy, @updatedBy, @sequence,
                @name, @phone, @weChat, @email, @lastOrderAt, @completedOrderCount, @totalSpend);";

        await connection.ExecuteAsync(sql, new
        {
            id,
            workspaceId,
            createdAt = dto.CreatedAtUtc,
            updatedAt = dto.UpdatedAtUtc,
            createdBy = userId,
            updatedBy = userId,
            sequence,
            name = dto.Name.Trim(),
            phone = dto.Phone,
            weChat = dto.WeChat,
            email = dto.Email,
            lastOrderAt = dto.LastOrderAtUtc,
            completedOrderCount = dto.CompletedOrderCount,
            totalSpend = RoundMoney(dto.TotalSpend)
        }, transaction);
    }

    private static async Task InsertInventoryItemAsync(IDbConnection connection, IDbTransaction transaction, Guid workspaceId, Guid id, LocalInventoryItemDto dto, Guid? productId, Guid userId, long sequence)
    {
        const string sql = @"
            INSERT INTO ""CommerceInventoryItems"" (
                ""Id"", ""WorkspaceId"", ""CreatedAt"", ""UpdatedAt"", ""DeletedAt"", ""Lifecycle"",
                ""CustomFieldsJson"", ""Revision"", ""CreatedByUserId"", ""UpdatedByUserId"", ""LastChangeSequence"",
                ""Name"", ""Sku"", ""ProductId"", ""ProductVariantId"", ""UnitId"", ""QuantityAvailable"", ""ReorderThreshold"", ""UnitCost"")
            VALUES (
                @id, @workspaceId, @createdAt, @updatedAt, NULL, 0,
                NULL, 1, @createdBy, @updatedBy, @sequence,
                @name, @sku, @productId, @productVariantId, @unitId, @quantityAvailable, @reorderThreshold, @unitCost);";

        await connection.ExecuteAsync(sql, new
        {
            id,
            workspaceId,
            createdAt = dto.CreatedAtUtc,
            updatedAt = dto.UpdatedAtUtc,
            createdBy = userId,
            updatedBy = userId,
            sequence,
            name = dto.Name.Trim(),
            sku = dto.Sku,
            productId,
            productVariantId = dto.ProductVariantId,
            unitId = dto.UnitId,
            quantityAvailable = RoundQuantity(dto.QuantityAvailable),
            reorderThreshold = RoundQuantity(dto.ReorderThreshold),
            unitCost = dto.UnitCost.HasValue ? RoundMoney(dto.UnitCost.Value) : (decimal?)null
        }, transaction);
    }

    private static async Task InsertOrderAsync(IDbConnection connection, IDbTransaction transaction, Guid workspaceId, Guid id, LocalOrderDto dto, Guid? customerId, Guid userId, long sequence)
    {
        const string sql = @"
            INSERT INTO ""CommerceOrders"" (
                ""Id"", ""WorkspaceId"", ""CreatedAt"", ""UpdatedAt"", ""DeletedAt"", ""Lifecycle"",
                ""CustomFieldsJson"", ""Revision"", ""CreatedByUserId"", ""UpdatedByUserId"", ""LastChangeSequence"",
                ""OrderNo"", ""CustomerId"", ""SalesStage"", ""PaymentStage"", ""FulfillmentStage"",
                ""Subtotal"", ""Total"", ""Cost"", ""GrossProfit"", ""GrossMargin"", ""PaidAmount"", ""ReceivableAmount"", ""OrderedAt"", ""Note"")
            VALUES (
                @id, @workspaceId, @createdAt, @updatedAt, NULL, 0,
                NULL, 1, @createdBy, @updatedBy, @sequence,
                @orderNo, @customerId, @salesStage, @paymentStage, @fulfillmentStage,
                @subtotal, @total, @cost, @grossProfit, @grossMargin, @paidAmount, @receivableAmount, @orderedAt, @note);";

        await connection.ExecuteAsync(sql, new
        {
            id,
            workspaceId,
            createdAt = dto.CreatedAtUtc,
            updatedAt = dto.UpdatedAtUtc,
            createdBy = userId,
            updatedBy = userId,
            sequence,
            orderNo = dto.OrderNo.Trim(),
            customerId,
            salesStage = (int)dto.SalesStage,
            paymentStage = (int)dto.PaymentStage,
            fulfillmentStage = (int)dto.FulfillmentStage,
            subtotal = RoundMoney(dto.Subtotal),
            total = RoundMoney(dto.Total),
            cost = dto.Cost.HasValue ? RoundMoney(dto.Cost.Value) : (decimal?)null,
            grossProfit = dto.GrossProfit.HasValue ? RoundMoney(dto.GrossProfit.Value) : (decimal?)null,
            grossMargin = dto.GrossMargin,
            paidAmount = RoundMoney(dto.PaidAmount),
            receivableAmount = RoundMoney(dto.ReceivableAmount),
            orderedAt = dto.OrderedAtUtc,
            note = dto.Note
        }, transaction);
    }

    private static async Task InsertOrderItemAsync(IDbConnection connection, IDbTransaction transaction, Guid workspaceId, Guid id, LocalOrderItemDto dto, Guid orderId, Guid? productId, Guid? inventoryItemId, Guid userId, long sequence)
    {
        const string sql = @"
            INSERT INTO ""CommerceOrderItems"" (
                ""Id"", ""WorkspaceId"", ""CreatedAt"", ""UpdatedAt"", ""DeletedAt"", ""Lifecycle"",
                ""CustomFieldsJson"", ""Revision"", ""CreatedByUserId"", ""UpdatedByUserId"", ""LastChangeSequence"",
                ""OrderId"", ""ProductId"", ""ProductVariantId"", ""InventoryItemId"", ""UnitId"", ""Description"", ""Quantity"", ""UnitPrice"", ""UnitCost"", ""LineTotal"")
            VALUES (
                @id, @workspaceId, @createdAt, @updatedAt, NULL, 0,
                NULL, 1, @createdBy, @updatedBy, @sequence,
                @orderId, @productId, @productVariantId, @inventoryItemId, @unitId, @description, @quantity, @unitPrice, @unitCost, @lineTotal);";

        await connection.ExecuteAsync(sql, new
        {
            id,
            workspaceId,
            createdAt = DateTime.UtcNow,
            updatedAt = DateTime.UtcNow,
            createdBy = userId,
            updatedBy = userId,
            sequence,
            orderId,
            productId,
            productVariantId = dto.ProductVariantId,
            inventoryItemId,
            unitId = dto.UnitId,
            description = dto.Description,
            quantity = RoundQuantity(dto.Quantity),
            unitPrice = RoundMoney(dto.UnitPrice),
            unitCost = dto.UnitCost.HasValue ? RoundMoney(dto.UnitCost.Value) : (decimal?)null,
            lineTotal = RoundMoney(dto.LineTotal)
        }, transaction);
    }

    private static async Task InsertCashFlowEntryAsync(IDbConnection connection, IDbTransaction transaction, Guid workspaceId, Guid id, LocalCashFlowEntryDto dto, Guid? orderId, Guid userId, long sequence, Guid batchId)
    {
        const string sql = @"
            INSERT INTO ""CommerceCashFlowEntries"" (
                ""Id"", ""WorkspaceId"", ""CreatedAt"", ""UpdatedAt"", ""DeletedAt"", ""Lifecycle"",
                ""CustomFieldsJson"", ""Revision"", ""CreatedByUserId"", ""UpdatedByUserId"", ""LastChangeSequence"",
                ""Direction"", ""Amount"", ""SettledAmount"", ""SettlementStatus"", ""OccurredAt"", ""DueDate"",
                ""CategoryName"", ""OrderId"", ""PaymentRecordId"", ""ImportBatchId"", ""SourceRowKey"", ""BusinessKey"")
            VALUES (
                @id, @workspaceId, @createdAt, @updatedAt, NULL, 0,
                NULL, 1, @createdBy, @updatedBy, @sequence,
                @direction, @amount, @settledAmount, @settlementStatus, @occurredAt, @dueDate,
                @categoryName, @orderId, NULL, @batchId, NULL, @businessKey);";

        await connection.ExecuteAsync(sql, new
        {
            id,
            workspaceId,
            createdAt = DateTime.UtcNow,
            updatedAt = DateTime.UtcNow,
            createdBy = userId,
            updatedBy = userId,
            sequence,
            direction = (int)dto.Direction,
            amount = RoundMoney(dto.Amount),
            settledAmount = RoundMoney(dto.SettledAmount),
            settlementStatus = (int)dto.SettlementStatus,
            occurredAt = dto.OccurredAtUtc,
            dueDate = dto.DueDateUtc,
            categoryName = dto.CategoryName,
            orderId,
            batchId,
            businessKey = dto.BusinessKey
        }, transaction);
    }

    private static async Task InsertPaymentRecordAsync(IDbConnection connection, IDbTransaction transaction, Guid workspaceId, Guid id, LocalPaymentRecordDto dto, Guid? orderId, Guid? cashFlowEntryId, Guid userId, long sequence)
    {
        const string sql = @"
            INSERT INTO ""CommercePaymentRecords"" (
                ""Id"", ""WorkspaceId"", ""CreatedAt"", ""UpdatedAt"", ""DeletedAt"", ""Lifecycle"",
                ""CustomFieldsJson"", ""Revision"", ""CreatedByUserId"", ""UpdatedByUserId"", ""LastChangeSequence"",
                ""OrderId"", ""CashFlowEntryId"", ""Amount"", ""PaidAt"", ""Method"", ""BusinessKey"")
            VALUES (
                @id, @workspaceId, @createdAt, @updatedAt, NULL, 0,
                NULL, 1, @createdBy, @updatedBy, @sequence,
                @orderId, @cashFlowEntryId, @amount, @paidAt, @method, @businessKey);";

        await connection.ExecuteAsync(sql, new
        {
            id,
            workspaceId,
            createdAt = DateTime.UtcNow,
            updatedAt = DateTime.UtcNow,
            createdBy = userId,
            updatedBy = userId,
            sequence,
            orderId,
            cashFlowEntryId,
            amount = RoundMoney(dto.Amount),
            paidAt = dto.PaidAtUtc,
            method = dto.Method,
            businessKey = dto.BusinessKey
        }, transaction);
    }

    private static Guid? ResolveReference(string? sourceLocalId, Guid? explicitId, Dictionary<string, Guid> map)
    {
        if (!string.IsNullOrWhiteSpace(sourceLocalId) && map.TryGetValue(sourceLocalId, out var mapped))
            return mapped;
        return explicitId;
    }

    private static async Task<bool> ExistsAsync(IDbConnection connection, IDbTransaction transaction, string tableName, Guid id)
    {
        var count = await connection.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM \"{tableName}\" WHERE \"Id\" = @id;",
            new { id },
            transaction);
        return count > 0;
    }

    private async Task RecordChangeAsync(IDbConnection connection, IDbTransaction transaction, Guid workspaceId, long sequence, string entityType, Guid entityId, string action, long revision)
    {
        await _syncService.RecordChangeAsync(connection, transaction, workspaceId, sequence, entityType, entityId, action, revision, _currentUser.UserId, null);
    }

    private static decimal RoundMoney(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    private static decimal RoundQuantity(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private sealed class ResolvedImport
    {
        public Dictionary<string, Guid> Products { get; } = new();
        public Dictionary<string, Guid> Customers { get; } = new();
        public Dictionary<string, Guid> InventoryItems { get; } = new();
        public Dictionary<string, Guid> Orders { get; } = new();
        public Dictionary<string, Guid> OrderItems { get; } = new();
        public Dictionary<string, Guid> PaymentRecords { get; } = new();
        public Dictionary<string, Guid> CashFlowEntries { get; } = new();
    }

    private sealed class CloudImportBatchRecord
    {
        public Guid Id { get; set; }
        public Guid WorkspaceId { get; set; }
        public Guid SourceInstanceId { get; set; }
        public string SourceFingerprint { get; set; } = string.Empty;
        public string SourceReportJson { get; set; } = string.Empty;
        public string? ResultJson { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
    }

    private sealed class ImportBatchReport
    {
        public string Fingerprint { get; set; } = string.Empty;
        public LocalImportPackage Package { get; set; } = new();
    }
}
