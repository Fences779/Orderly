using System.Data;
using Dapper;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Permissions;
using Orderly.Core.Commerce;
using Orderly.Server.Mapping;
using Orderly.Server.Models;

namespace Orderly.Server.Services;

public partial class CommerceCommandService
{
    public async Task<CommandResult<CloudEntityDto>> ArchiveAsync(Guid workspaceId, string entityType, Guid entityId, ArchiveCommand command, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");
        var membership = await GetMembershipAsync(userId);

        var (tableName, entityTypeConstant) = ResolveEntityTable(entityType);

        return await ExecuteWithIdempotencyAsync<ArchiveCommand, CloudEntityDto>(
            workspaceId,
            "archive",
            command,
            async (connection, transaction, sequence, collector, ct) =>
            {
                var row = await connection.QueryFirstOrDefaultAsync(
                    $"SELECT * FROM \"{tableName}\" WHERE \"Id\" = @entityId AND \"WorkspaceId\" = @workspaceId;",
                    new { entityId, workspaceId },
                    transaction)
                    ?? throw new InvalidOperationException($"记录不存在: {entityType}/{entityId}");

                long revision = (long)row.Revision;
                await ThrowIfRevisionMismatchAsync(connection, transaction, tableName, entityId, command.ExpectedRevision, ct);

                Guid? createdByUserId = row.CreatedByUserId;
                Guid? assignedToUserId = row.AssignedToUserId;

                if (!_permissions.CanArchive(membership, entityTypeConstant, createdByUserId, assignedToUserId))
                    throw new UnauthorizedAccessException("没有归档该记录的权限。");

                var beforeJson = await SnapshotJsonAsync(MapEntityDto(row, entityTypeConstant, membership));
                var now = DateTime.UtcNow;

                await connection.ExecuteAsync(
                    $@"UPDATE ""{tableName}""
                     SET ""Lifecycle"" = @archived,
                         ""DeletedAt"" = @now,
                         ""ArchivedByUserId"" = @archivedBy,
                         ""ArchiveReason"" = @reason,
                         ""Revision"" = ""Revision"" + 1,
                         ""UpdatedAt"" = @now,
                         ""UpdatedByUserId"" = @updatedBy,
                         ""LastChangeSequence"" = @sequence
                     WHERE ""Id"" = @entityId;",
                    new
                    {
                        archived = (int)EntityLifecycleStatus.Archived,
                        now,
                        archivedBy = userId,
                        reason = command.ArchiveReason,
                        updatedBy = userId,
                        sequence,
                        entityId
                    },
                    transaction);

                var dto = MapEntityDto(row, entityTypeConstant, membership);
                dto.Lifecycle = EntityLifecycleStatus.Archived;
                dto.Revision = revision + 1;
                dto.UpdatedAtUtc = now;
                var afterJson = await SnapshotJsonAsync(dto);
                await AuditAsync(connection, transaction, workspaceId, "Archived", entityTypeConstant, entityId, beforeJson, afterJson, command.ArchiveReason, command.ClientRequestId, collector);
                await RecordChangeAsync(connection, transaction, workspaceId, sequence, entityTypeConstant, entityId, "archived", dto.Revision);

                return (dto, entityTypeConstant, entityId);
            },
            cancellationToken);
    }

    public async Task<CommandResult<CloudEntityDto>> RecoverAsync(Guid workspaceId, string entityType, Guid entityId, WriteCommandBase command, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");
        var membership = await GetMembershipAsync(userId);

        if (!_permissions.IsAdmin(membership))
            throw new UnauthorizedAccessException("只有管理员可以恢复归档数据。");

        var (tableName, entityTypeConstant) = ResolveEntityTable(entityType);

        return await ExecuteWithIdempotencyAsync<WriteCommandBase, CloudEntityDto>(
            workspaceId,
            "recover",
            command,
            async (connection, transaction, sequence, collector, ct) =>
            {
                await ThrowIfRevisionMismatchAsync(connection, transaction, tableName, entityId, command.ExpectedRevision, ct);

                var row = await connection.QueryFirstOrDefaultAsync(
                    $"SELECT * FROM \"{tableName}\" WHERE \"Id\" = @entityId AND \"WorkspaceId\" = @workspaceId;",
                    new { entityId, workspaceId },
                    transaction)
                    ?? throw new InvalidOperationException($"记录不存在: {entityType}/{entityId}");

                long revision = (long)row.Revision;
                var beforeJson = await SnapshotJsonAsync(MapEntityDto(row, entityTypeConstant, membership));
                var now = DateTime.UtcNow;

                await connection.ExecuteAsync(
                    $@"UPDATE ""{tableName}""
                     SET ""Lifecycle"" = @active,
                         ""DeletedAt"" = NULL,
                         ""ArchiveReason"" = NULL,
                         ""Revision"" = ""Revision"" + 1,
                         ""UpdatedAt"" = @now,
                         ""UpdatedByUserId"" = @updatedBy,
                         ""LastChangeSequence"" = @sequence
                     WHERE ""Id"" = @entityId;",
                    new
                    {
                        active = (int)EntityLifecycleStatus.Active,
                        now,
                        updatedBy = userId,
                        sequence,
                        entityId
                    },
                    transaction);

                var dto = MapEntityDto(row, entityTypeConstant, membership);
                dto.Lifecycle = EntityLifecycleStatus.Active;
                dto.Revision = revision + 1;
                dto.UpdatedAtUtc = now;
                var afterJson = await SnapshotJsonAsync(dto);
                await AuditAsync(connection, transaction, workspaceId, "Recovered", entityTypeConstant, entityId, beforeJson, afterJson, command.Reason, command.ClientRequestId, collector);
                await RecordChangeAsync(connection, transaction, workspaceId, sequence, entityTypeConstant, entityId, "recovered", dto.Revision);

                return (dto, entityTypeConstant, entityId);
            },
            cancellationToken);
    }

    private static (string TableName, string EntityType) ResolveEntityTable(string entityType)
        => entityType.ToLowerInvariant() switch
        {
            "order" or "orders" => ("CommerceOrders", EntityType.Order),
            "product" or "products" => ("CommerceProducts", EntityType.Product),
            "inventoryitem" or "inventory" => ("CommerceInventoryItems", EntityType.InventoryItem),
            "customer" or "customers" => ("CommerceCustomers", EntityType.Customer),
            "cashflow" or "cashflowentry" => ("CommerceCashFlowEntries", EntityType.CashFlowEntry),
            "task" or "tasks" => ("CommerceBusinessTasks", EntityType.BusinessTask),
            _ => throw new InvalidOperationException($"不支持的实体类型: {entityType}")
        };

    private CloudEntityDto MapEntityDto(dynamic row, string entityType, CloudWorkspaceMemberRecord membership)
        => entityType switch
        {
            EntityType.Order => CommerceDtoMapper.ToOrderDto(row, _permissions.CanViewCosts(membership)),
            EntityType.Product => CommerceDtoMapper.ToProductDto(row, _permissions.CanViewCosts(membership)),
            EntityType.InventoryItem => CommerceDtoMapper.ToInventoryItemDto(row),
            EntityType.Customer => CommerceDtoMapper.ToCustomerDto(row),
            EntityType.CashFlowEntry => CommerceDtoMapper.ToCashFlowEntryDto(row),
            _ => throw new InvalidOperationException($"不支持的实体类型: {entityType}")
        };
}
