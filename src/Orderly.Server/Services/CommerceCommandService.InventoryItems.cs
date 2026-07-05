using System.Data;
using Dapper;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Permissions;
using Orderly.Server.Mapping;

namespace Orderly.Server.Services;

public partial class CommerceCommandService
{
    public async Task<CommandResult<CloudInventoryItemDto>> CreateInventoryItemAsync(Guid workspaceId, CreateInventoryItemCommand command, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");
        var membership = await GetMembershipAsync(userId);

        if (!_permissions.IsAdmin(membership))
            throw new UnauthorizedAccessException("没有库存项管理权限。");

        if (string.IsNullOrWhiteSpace(command.Name))
            throw new InvalidOperationException("库存项名称不能为空。");

        if (command.QuantityAvailable < 0m)
            throw new InvalidOperationException("可用数量不能为负数。");

        return await ExecuteWithIdempotencyAsync<CreateInventoryItemCommand, CloudInventoryItemDto>(
            workspaceId,
            "inventoryItem:create",
            command,
            async (connection, transaction, sequence, collector, ct) =>
            {
                var now = DateTime.UtcNow;
                var itemId = Guid.NewGuid();

                await connection.ExecuteAsync(
                    @"INSERT INTO ""CommerceInventoryItems"" (
                        ""Id"", ""WorkspaceId"", ""CreatedAt"", ""UpdatedAt"", ""DeletedAt"", ""Lifecycle"",
                        ""CustomFieldsJson"", ""Revision"", ""CreatedByUserId"", ""UpdatedByUserId"", ""LastChangeSequence"",
                        ""Name"", ""Sku"", ""ProductId"", ""ProductVariantId"", ""UnitId"", ""QuantityAvailable"", ""ReorderThreshold"", ""UnitCost"")
                    VALUES (
                        @id, @workspaceId, @now, @now, NULL, 0,
                        NULL, 1, @createdBy, @updatedBy, @sequence,
                        @name, @sku, @productId, @productVariantId, @unitId, @quantityAvailable, @reorderThreshold, @unitCost);",
                    new
                    {
                        id = itemId,
                        workspaceId,
                        now,
                        createdBy = userId,
                        updatedBy = userId,
                        sequence,
                        name = command.Name.Trim(),
                        sku = command.Sku,
                        productId = command.ProductId,
                        productVariantId = command.ProductVariantId,
                        unitId = command.UnitId,
                        quantityAvailable = RoundQuantity(command.QuantityAvailable),
                        reorderThreshold = RoundQuantity(command.ReorderThreshold),
                        unitCost = command.UnitCost.HasValue ? RoundMoney(command.UnitCost.Value) : (decimal?)null
                    },
                    transaction);

                var dto = await LoadInventoryItemDtoAsync(connection, transaction, workspaceId, itemId, ct);
                var afterJson = await SnapshotJsonAsync(dto);
                await AuditAsync(connection, transaction, workspaceId, "InventoryItemCreated", EntityType.InventoryItem, itemId, null, afterJson, command.Reason, command.ClientRequestId, collector);
                await RecordChangeAsync(connection, transaction, workspaceId, sequence, EntityType.InventoryItem, itemId, "created", dto.Revision);

                return (dto, EntityType.InventoryItem, itemId);
            },
            cancellationToken);
    }

    public async Task<CommandResult<CloudInventoryItemDto>> UpdateInventoryItemAsync(Guid workspaceId, Guid itemId, UpdateInventoryItemCommand command, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");
        var membership = await GetMembershipAsync(userId);

        if (!_permissions.IsAdmin(membership))
            throw new UnauthorizedAccessException("没有库存项管理权限。");

        if (command.QuantityAvailable.HasValue && command.QuantityAvailable.Value < 0m)
            throw new InvalidOperationException("可用数量不能为负数。");

        return await ExecuteWithIdempotencyAsync<UpdateInventoryItemCommand, CloudInventoryItemDto>(
            workspaceId,
            "inventoryItem:update",
            command,
            async (connection, transaction, sequence, collector, ct) =>
            {
                await ThrowIfRevisionMismatchAsync(connection, transaction, "CommerceInventoryItems", itemId, command.ExpectedRevision, ct);

                var before = await LoadInventoryItemDtoAsync(connection, transaction, workspaceId, itemId, ct);
                var beforeJson = await SnapshotJsonAsync(before);

                await connection.ExecuteAsync(
                    @"UPDATE ""CommerceInventoryItems""
                     SET ""UpdatedAt"" = @now,
                         ""UpdatedByUserId"" = @updatedBy,
                         ""Revision"" = ""Revision"" + 1,
                         ""LastChangeSequence"" = @sequence,
                         ""Name"" = COALESCE(NULLIF(@name, ''), ""Name""),
                         ""Sku"" = COALESCE(@sku, ""Sku""),
                         ""ProductId"" = COALESCE(@productId, ""ProductId""),
                         ""ProductVariantId"" = COALESCE(@productVariantId, ""ProductVariantId""),
                         ""UnitId"" = COALESCE(@unitId, ""UnitId""),
                         ""QuantityAvailable"" = COALESCE(@quantityAvailable, ""QuantityAvailable""),
                         ""ReorderThreshold"" = COALESCE(@reorderThreshold, ""ReorderThreshold""),
                         ""UnitCost"" = COALESCE(@unitCost, ""UnitCost"")
                     WHERE ""Id"" = @itemId;",
                    new
                    {
                        now = DateTime.UtcNow,
                        updatedBy = userId,
                        sequence,
                        itemId,
                        name = command.Name,
                        sku = command.Sku,
                        productId = command.ProductId,
                        productVariantId = command.ProductVariantId,
                        unitId = command.UnitId,
                        quantityAvailable = command.QuantityAvailable.HasValue ? RoundQuantity(command.QuantityAvailable.Value) : (decimal?)null,
                        reorderThreshold = command.ReorderThreshold.HasValue ? RoundQuantity(command.ReorderThreshold.Value) : (decimal?)null,
                        unitCost = command.UnitCost.HasValue ? RoundMoney(command.UnitCost.Value) : (decimal?)null
                    },
                    transaction);

                var dto = await LoadInventoryItemDtoAsync(connection, transaction, workspaceId, itemId, ct);
                var afterJson = await SnapshotJsonAsync(dto);
                await AuditAsync(connection, transaction, workspaceId, "InventoryItemUpdated", EntityType.InventoryItem, itemId, beforeJson, afterJson, command.Reason, command.ClientRequestId, collector);
                await RecordChangeAsync(connection, transaction, workspaceId, sequence, EntityType.InventoryItem, itemId, "updated", dto.Revision);

                return (dto, EntityType.InventoryItem, itemId);
            },
            cancellationToken);
    }

    private async Task<CloudInventoryItemDto> LoadInventoryItemDtoAsync(IDbConnection connection, IDbTransaction transaction, Guid workspaceId, Guid itemId, CancellationToken cancellationToken)
    {
        var row = await connection.QueryFirstOrDefaultAsync(
            "SELECT * FROM \"CommerceInventoryItems\" WHERE \"WorkspaceId\" = @workspaceId AND \"Id\" = @itemId;",
            new { workspaceId, itemId },
            transaction)
            ?? throw new InvalidOperationException($"库存项 {itemId} 不存在。");

        return CommerceDtoMapper.ToInventoryItemDto(row);
    }
}
