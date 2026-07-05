using System.Data;
using Dapper;
using Npgsql;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Permissions;
using Orderly.Contracts.Realtime;
using Orderly.Core.Commerce;
using Orderly.Server.Mapping;

namespace Orderly.Server.Services;

public partial class CommerceCommandService
{
    public async Task<CommandResult<CloudInventoryMovementDto>> RecordInventoryMovementAsync(Guid workspaceId, InventoryMovementCommand command, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");
        var membership = await GetMembershipAsync(userId);

        if (!_permissions.CanRecordInventoryMovement(membership))
            throw new UnauthorizedAccessException("没有库存操作权限。");

        if (command.Quantity <= 0m)
            throw new InvalidOperationException("库存变动数量必须大于 0。");

        return await ExecuteWithIdempotencyAsync<InventoryMovementCommand, CloudInventoryMovementDto>(
            workspaceId,
            "inventory:movement",
            command,
            async (connection, transaction, sequence, collector, ct) =>
            {
                var now = DateTime.UtcNow;

                var inventoryRow = await connection.QueryFirstOrDefaultAsync(
                    @"SELECT * FROM ""CommerceInventoryItems""
                     WHERE ""Id"" = @inventoryItemId AND ""WorkspaceId"" = @workspaceId
                     FOR UPDATE;",
                    new { command.InventoryItemId, workspaceId },
                    transaction)
                    ?? throw new InvalidOperationException($"库存项 {command.InventoryItemId} 不存在。");

                var beforeQty = (decimal)inventoryRow.QuantityAvailable;
                var delta = SignedDelta(command.MovementType, command.Quantity);
                var afterQty = beforeQty + delta;

                if (afterQty < 0m)
                    throw new InvalidOperationException("库存变动后数量不能为负数。");

                await connection.ExecuteAsync(
                    @"UPDATE ""CommerceInventoryItems""
                     SET ""QuantityAvailable"" = ""QuantityAvailable"" + @delta,
                         ""Revision"" = ""Revision"" + 1,
                         ""UpdatedAt"" = @now,
                         ""UpdatedByUserId"" = @updatedBy,
                         ""LastChangeSequence"" = @sequence
                     WHERE ""Id"" = @inventoryItemId;",
                    new { delta, now, updatedBy = userId, sequence, command.InventoryItemId },
                    transaction);

                var movementId = Guid.NewGuid();
                await connection.ExecuteAsync(
                    @"INSERT INTO ""CommerceInventoryMovements"" (
                        ""Id"", ""WorkspaceId"", ""CreatedAt"", ""UpdatedAt"", ""DeletedAt"", ""Lifecycle"",
                        ""Revision"", ""CreatedByUserId"", ""UpdatedByUserId"", ""LastChangeSequence"",
                        ""InventoryItemId"", ""MovementType"", ""Quantity"", ""SupplierId"", ""OrderId"", ""OccurredAt"", ""BusinessKey"", ""Note"")
                    VALUES (
                        @id, @workspaceId, @now, @now, NULL, 0,
                        1, @createdBy, @updatedBy, @sequence,
                        @inventoryItemId, @movementType, @quantity, @supplierId, @orderId, @occurredAt, @businessKey, @note);",
                    new
                    {
                        id = movementId,
                        workspaceId,
                        now,
                        createdBy = userId,
                        updatedBy = userId,
                        sequence,
                        command.InventoryItemId,
                        movementType = (int)command.MovementType,
                        quantity = RoundQuantity(command.Quantity),
                        command.SupplierId,
                        command.OrderId,
                        occurredAt = command.OccurredAtUtc,
                        businessKey = command.BusinessKey,
                        command.Note
                    },
                    transaction);

                await connection.ExecuteAsync(
                    @"INSERT INTO ""CloudInventoryMovementAudits"" (
                        ""Id"", ""WorkspaceId"", ""InventoryItemId"", ""MovementId"",
                        ""MovementType"", ""QuantityBefore"", ""QuantityDelta"", ""QuantityAfter"",
                        ""Reason"", ""IsStocktake"", ""ActorUserId"", ""OccurredAt"")
                    VALUES (
                        @id, @workspaceId, @inventoryItemId, @movementId,
                        @movementType, @quantityBefore, @quantityDelta, @quantityAfter,
                        @reason, @isStocktake, @actorUserId, @now);",
                    new
                    {
                        id = Guid.NewGuid(),
                        workspaceId,
                        command.InventoryItemId,
                        movementId,
                        movementType = command.MovementType.ToString(),
                        quantityBefore = beforeQty,
                        quantityDelta = delta,
                        quantityAfter = afterQty,
                        reason = command.Reason,
                        isStocktake = command.IsStocktake,
                        actorUserId = userId,
                        now
                    },
                    transaction);

                var dto = await LoadInventoryMovementDtoAsync(connection, transaction, workspaceId, movementId, ct);
                var afterJson = await SnapshotJsonAsync(dto);
                await AuditAsync(connection, transaction, workspaceId, command.IsStocktake ? "StocktakeAdjustment" : "InventoryMovementCreated", EntityType.InventoryItem, command.InventoryItemId, null, afterJson, command.Reason, command.ClientRequestId, collector);
                await RecordChangeAsync(connection, transaction, workspaceId, sequence, EntityType.InventoryItem, command.InventoryItemId, command.IsStocktake ? "stocktakeAdjusted" : "movementCreated", inventoryRow.Revision + 1);

                return (dto, EntityType.InventoryItem, command.InventoryItemId);
            },
            cancellationToken);
    }

    private static decimal SignedDelta(InventoryMovementType movementType, decimal quantity)
        => movementType switch
        {
            InventoryMovementType.Inbound => quantity,
            InventoryMovementType.Outbound => -quantity,
            InventoryMovementType.Adjustment => quantity,
            _ => throw new ArgumentOutOfRangeException(nameof(movementType), movementType, "Unknown inventory movement type.")
        };

    private async Task<CloudInventoryMovementDto> LoadInventoryMovementDtoAsync(IDbConnection connection, IDbTransaction transaction, Guid workspaceId, Guid movementId, CancellationToken cancellationToken)
    {
        var row = await connection.QueryFirstOrDefaultAsync(
            "SELECT * FROM \"CommerceInventoryMovements\" WHERE \"WorkspaceId\" = @workspaceId AND \"Id\" = @movementId;",
            new { workspaceId, movementId },
            transaction)
            ?? throw new InvalidOperationException($"库存流水 {movementId} 不存在。");

        return CommerceDtoMapper.ToInventoryMovementDto(row);
    }
}
