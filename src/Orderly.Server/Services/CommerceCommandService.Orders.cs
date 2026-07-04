using System.Data;
using System.Text.Json;
using Dapper;
using Npgsql;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Permissions;
using Orderly.Contracts.Realtime;
using Orderly.Core.Commerce;
using Orderly.Server.Mapping;
using Orderly.Server.Models;

namespace Orderly.Server.Services;

public partial class CommerceCommandService
{
    public async Task<CommandResult<CloudOrderDto>> CreateOrderAsync(Guid workspaceId, CreateOrderCommand command, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");
        var membership = await GetMembershipAsync(userId);

        if (!command.Items.Any())
            throw new InvalidOperationException("订单必须包含至少一个订单项。");

        return await ExecuteWithIdempotencyAsync<CreateOrderCommand, CloudOrderDto>(
            workspaceId,
            "order:create",
            command,
            async (connection, transaction, sequence, ct) =>
            {
                var now = DateTime.UtcNow;
                var orderId = Guid.NewGuid();

                // Ensure order number uniqueness within the workspace.
                var existingOrder = await connection.ExecuteScalarAsync<Guid?>(
                    @"SELECT ""Id"" FROM ""CommerceOrders""
                     WHERE ""WorkspaceId"" = @workspaceId AND ""OrderNo"" = @orderNo AND ""DeletedAt"" IS NULL;",
                    new { workspaceId, command.OrderNo },
                    transaction);
                if (existingOrder.HasValue)
                    throw new InvalidOperationException($"订单编号 {command.OrderNo} 已存在。");

                var (subtotal, total, cost, grossProfit, grossMargin) = ComputeOrderTotals(command.Items);

                await connection.ExecuteAsync(
                    @"INSERT INTO ""CommerceOrders"" (
                        ""Id"", ""WorkspaceId"", ""CreatedAt"", ""UpdatedAt"", ""DeletedAt"", ""Lifecycle"",
                        ""CustomFieldsJson"", ""Revision"", ""CreatedByUserId"", ""UpdatedByUserId"", ""LastChangeSequence"",
                        ""OrderNo"", ""CustomerId"", ""SalesStage"", ""PaymentStage"", ""FulfillmentStage"",
                        ""Subtotal"", ""Total"", ""Cost"", ""GrossProfit"", ""GrossMargin"",
                        ""PaidAmount"", ""ReceivableAmount"", ""OrderedAt"", ""Note"", ""AssignedToUserId"")
                    VALUES (
                        @orderId, @workspaceId, @now, @now, NULL, 0,
                        NULL, 1, @createdBy, @updatedBy, @sequence,
                        @orderNo, @customerId, @salesStage, @paymentStage, @fulfillmentStage,
                        @subtotal, @total, @cost, @grossProfit, @grossMargin,
                        0, @receivable, @orderedAt, @note, NULL);",
                    new
                    {
                        orderId,
                        workspaceId,
                        now,
                        createdBy = userId,
                        updatedBy = userId,
                        sequence,
                        command.OrderNo,
                        command.CustomerId,
                        salesStage = (int)command.SalesStage,
                        paymentStage = (int)command.PaymentStage,
                        fulfillmentStage = (int)command.FulfillmentStage,
                        subtotal,
                        total,
                        cost,
                        grossProfit,
                        grossMargin,
                        receivable = total,
                        command.OrderedAtUtc,
                        command.Note
                    },
                    transaction);

                await InsertOrderItemsAsync(connection, transaction, workspaceId, orderId, command.Items, sequence, userId, now, ct);

                var dto = await LoadOrderDtoAsync(connection, transaction, workspaceId, orderId, membership, ct);
                var afterJson = await SnapshotJsonAsync(dto);
                await AuditAsync(connection, transaction, workspaceId, "OrderCreated", EntityType.Order, orderId, null, afterJson, command.Reason, command.ClientRequestId);
                await RecordChangeAsync(connection, transaction, workspaceId, sequence, EntityType.Order, orderId, "created", dto.Revision);

                return (dto, EntityType.Order, orderId);
            },
            cancellationToken);
    }

    public async Task<CommandResult<CloudOrderDto>> UpdateOrderAsync(Guid workspaceId, Guid orderId, UpdateOrderCommand command, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");
        var membership = await GetMembershipAsync(userId);

        return await ExecuteWithIdempotencyAsync<UpdateOrderCommand, CloudOrderDto>(
            workspaceId,
            "order:update",
            command,
            async (connection, transaction, sequence, ct) =>
            {
                await ThrowIfRevisionMismatchAsync(connection, transaction, "CommerceOrders", orderId, command.ExpectedRevision, ct);

                var before = await LoadOrderDtoAsync(connection, transaction, workspaceId, orderId, membership, ct);
                var beforeJson = await SnapshotJsonAsync(before);
                var now = DateTime.UtcNow;

                await connection.ExecuteAsync(
                    @"UPDATE ""CommerceOrders""
                     SET ""UpdatedAt"" = @now,
                         ""UpdatedByUserId"" = @updatedBy,
                         ""Revision"" = ""Revision"" + 1,
                         ""LastChangeSequence"" = @sequence,
                         ""CustomerId"" = COALESCE(@customerId, ""CustomerId""),
                         ""SalesStage"" = COALESCE(@salesStage, ""SalesStage""),
                         ""PaymentStage"" = COALESCE(@paymentStage, ""PaymentStage""),
                         ""FulfillmentStage"" = COALESCE(@fulfillmentStage, ""FulfillmentStage""),
                         ""Note"" = COALESCE(@note, ""Note""),
                         ""AssignedToUserId"" = COALESCE(@assignedToUserId, ""AssignedToUserId"")
                     WHERE ""Id"" = @orderId;",
                    new
                    {
                        now,
                        updatedBy = userId,
                        sequence,
                        orderId,
                        command.CustomerId,
                        salesStage = command.SalesStage.HasValue ? (int?)command.SalesStage.Value : null,
                        paymentStage = command.PaymentStage.HasValue ? (int?)command.PaymentStage.Value : null,
                        fulfillmentStage = command.FulfillmentStage.HasValue ? (int?)command.FulfillmentStage.Value : null,
                        command.Note,
                        assignedToUserId = command.AssignedToUserId
                    },
                    transaction);

                var dto = await LoadOrderDtoAsync(connection, transaction, workspaceId, orderId, membership, ct);
                var afterJson = await SnapshotJsonAsync(dto);
                await AuditAsync(connection, transaction, workspaceId, "OrderUpdated", EntityType.Order, orderId, beforeJson, afterJson, command.Reason, command.ClientRequestId);
                await RecordChangeAsync(connection, transaction, workspaceId, sequence, EntityType.Order, orderId, "updated", dto.Revision);

                return (dto, EntityType.Order, orderId);
            },
            cancellationToken);
    }

    public async Task<CommandResult<CloudOrderDto>> CompleteOrderAsync(Guid workspaceId, Guid orderId, CompleteOrderCommand command, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");
        var membership = await GetMembershipAsync(userId);

        return await ExecuteWithIdempotencyAsync<CompleteOrderCommand, CloudOrderDto>(
            workspaceId,
            "order:complete",
            command,
            async (connection, transaction, sequence, ct) =>
            {
                await ThrowIfRevisionMismatchAsync(connection, transaction, "CommerceOrders", orderId, command.ExpectedRevision, ct);

                var orderRow = await connection.QueryFirstOrDefaultAsync(
                    "SELECT * FROM \"CommerceOrders\" WHERE \"Id\" = @orderId;",
                    new { orderId },
                    transaction)
                    ?? throw new InvalidOperationException($"订单 {orderId} 不存在。");

                if ((int)orderRow.SalesStage == (int)OrderSalesStage.Completed)
                    throw new InvalidOperationException("该订单已经完成。");

                var before = await LoadOrderDtoAsync(connection, transaction, workspaceId, orderId, membership, ct);
                var beforeJson = await SnapshotJsonAsync(before);

                var itemRows = await connection.QueryAsync(
                    "SELECT * FROM \"CommerceOrderItems\" WHERE \"OrderId\" = @orderId AND \"DeletedAt\" IS NULL;",
                    new { orderId },
                    transaction);

                var requiredByItem = itemRows
                    .Where(r => r.InventoryItemId != null)
                    .GroupBy(r => (Guid)r.InventoryItemId)
                    .ToDictionary(g => g.Key, g => (decimal)g.Sum(x => (decimal)x.Quantity));

                var existingMovementKeys = (await connection.QueryAsync<string>(
                    @"SELECT ""BusinessKey"" FROM ""CommerceInventoryMovements""
                     WHERE ""OrderId"" = @orderId AND ""BusinessKey"" LIKE @keyPrefix;",
                    new { orderId, keyPrefix = $"order-completion:{orderId:N}:%" },
                    transaction)).ToHashSet();

                foreach (var inventoryItemId in requiredByItem.Keys.OrderBy(id => id.ToString("N")))
                {
                    var businessKey = $"order-completion:{orderId:N}:{inventoryItemId:N}";
                    if (existingMovementKeys.Contains(businessKey))
                        continue;

                    var required = requiredByItem[inventoryItemId];

                    var inventoryRow = await connection.QueryFirstOrDefaultAsync(
                        @"SELECT * FROM ""CommerceInventoryItems""
                         WHERE ""Id"" = @inventoryItemId AND ""WorkspaceId"" = @workspaceId
                         FOR UPDATE;",
                        new { inventoryItemId, workspaceId },
                        transaction)
                        ?? throw new InvalidOperationException($"库存项 {inventoryItemId} 不存在。");

                    var beforeQty = (decimal)inventoryRow.QuantityAvailable;
                    if (beforeQty < required)
                        throw new InvalidOperationException($"库存不足：{inventoryRow.Name} 需要 {required}，可用 {beforeQty}。");

                    var afterQty = beforeQty - required;
                    var updatedRows = await connection.ExecuteAsync(
                        @"UPDATE ""CommerceInventoryItems""
                         SET ""QuantityAvailable"" = ""QuantityAvailable"" - @required,
                             ""Revision"" = ""Revision"" + 1,
                             ""UpdatedAt"" = @now,
                             ""UpdatedByUserId"" = @updatedBy,
                             ""LastChangeSequence"" = @sequence
                         WHERE ""Id"" = @inventoryItemId AND ""QuantityAvailable"" >= @required;",
                        new { required, now = DateTime.UtcNow, updatedBy = userId, sequence, inventoryItemId },
                        transaction);

                    if (updatedRows != 1)
                        throw new InvalidOperationException($"库存扣减失败，可能库存不足或并发冲突：{inventoryRow.Name}");

                    var movementId = Guid.NewGuid();
                    await connection.ExecuteAsync(
                        @"INSERT INTO ""CommerceInventoryMovements"" (
                            ""Id"", ""WorkspaceId"", ""CreatedAt"", ""UpdatedAt"", ""DeletedAt"", ""Lifecycle"",
                            ""Revision"", ""CreatedByUserId"", ""UpdatedByUserId"", ""LastChangeSequence"",
                            ""InventoryItemId"", ""MovementType"", ""Quantity"", ""OrderId"", ""OccurredAt"", ""BusinessKey"")
                        VALUES (
                            @id, @workspaceId, @now, @now, NULL, 0,
                            1, @createdBy, @updatedBy, @sequence,
                            @inventoryItemId, @movementType, @quantity, @orderId, @occurredAt, @businessKey);",
                        new
                        {
                            id = movementId,
                            workspaceId,
                            now = DateTime.UtcNow,
                            createdBy = userId,
                            updatedBy = userId,
                            sequence,
                            inventoryItemId,
                            movementType = (int)InventoryMovementType.Outbound,
                            quantity = required,
                            orderId,
                            occurredAt = command.CompletedAtUtc,
                            businessKey
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
                            @reason, FALSE, @actorUserId, @now);",
                        new
                        {
                            id = Guid.NewGuid(),
                            workspaceId,
                            inventoryItemId,
                            movementId,
                            movementType = "Outbound",
                            quantityBefore = beforeQty,
                            quantityDelta = -required,
                            quantityAfter = afterQty,
                            reason = command.Reason,
                            actorUserId = userId,
                            now = DateTime.UtcNow
                        },
                        transaction);

                    await _notifier.NotifyAsync(workspaceId, RealtimeEvent.InventoryChanged, new RealtimeEventPayload
                    {
                        WorkspaceId = workspaceId,
                        EntityType = EntityType.InventoryItem,
                        EntityId = inventoryItemId,
                        Sequence = sequence,
                        ActorUserId = userId,
                        ActorDisplayName = _currentUser.DisplayName ?? string.Empty,
                        OccurredAtUtc = DateTime.UtcNow,
                        Action = "inventoryChanged"
                    });
                }

                await connection.ExecuteAsync(
                    @"UPDATE ""CommerceOrders""
                     SET ""SalesStage"" = @completed,
                         ""UpdatedAt"" = @now,
                         ""UpdatedByUserId"" = @updatedBy,
                         ""Revision"" = ""Revision"" + 1,
                         ""LastChangeSequence"" = @sequence
                     WHERE ""Id"" = @orderId;",
                    new { completed = (int)OrderSalesStage.Completed, now = DateTime.UtcNow, updatedBy = userId, sequence, orderId },
                    transaction);

                Guid? orderCustomerId = orderRow.CustomerId;
                if (orderCustomerId.HasValue)
                {
                    await RecomputeCustomerStatisticsAsync(connection, transaction, workspaceId, orderCustomerId.Value, sequence, userId, ct);
                }

                var dto = await LoadOrderDtoAsync(connection, transaction, workspaceId, orderId, membership, ct);
                var afterJson = await SnapshotJsonAsync(dto);
                await AuditAsync(connection, transaction, workspaceId, "OrderCompleted", EntityType.Order, orderId, beforeJson, afterJson, command.Reason, command.ClientRequestId);
                await RecordChangeAsync(connection, transaction, workspaceId, sequence, EntityType.Order, orderId, "completed", dto.Revision);

                return (dto, EntityType.Order, orderId);
            },
            cancellationToken);
    }

    public async Task<CommandResult<CloudOrderDto>> UpdateOrderStageAsync(Guid workspaceId, Guid orderId, OrderStageCommand command, string dimension, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");
        var membership = await GetMembershipAsync(userId);

        return await ExecuteWithIdempotencyAsync<OrderStageCommand, CloudOrderDto>(
            workspaceId,
            $"order:stage:{dimension}",
            command,
            async (connection, transaction, sequence, ct) =>
            {
                await ThrowIfRevisionMismatchAsync(connection, transaction, "CommerceOrders", orderId, command.ExpectedRevision, ct);

                var before = await LoadOrderDtoAsync(connection, transaction, workspaceId, orderId, membership, ct);
                var beforeJson = await SnapshotJsonAsync(before);

                var (columnName, parameterName, value) = dimension.ToLowerInvariant() switch
                {
                    "sales" when command.TargetSalesStage.HasValue => ("SalesStage", "targetStage", (int)command.TargetSalesStage.Value),
                    "payment" when command.TargetPaymentStage.HasValue => ("PaymentStage", "targetStage", (int)command.TargetPaymentStage.Value),
                    "fulfillment" when command.TargetFulfillmentStage.HasValue => ("FulfillmentStage", "targetStage", (int)command.TargetFulfillmentStage.Value),
                    _ => throw new InvalidOperationException($"不支持的订单阶段维度或目标阶段为空: {dimension}")
                };

                await connection.ExecuteAsync(
                    $@"UPDATE ""CommerceOrders""
                     SET ""UpdatedAt"" = @now,
                         ""UpdatedByUserId"" = @updatedBy,
                         ""Revision"" = ""Revision"" + 1,
                         ""LastChangeSequence"" = @sequence,
                         ""{columnName}"" = @targetStage
                     WHERE ""Id"" = @orderId;",
                    new
                    {
                        now = DateTime.UtcNow,
                        updatedBy = userId,
                        sequence,
                        orderId,
                        targetStage = value
                    },
                    transaction);

                var dto = await LoadOrderDtoAsync(connection, transaction, workspaceId, orderId, membership, ct);
                var afterJson = await SnapshotJsonAsync(dto);
                await AuditAsync(connection, transaction, workspaceId, $"Order{dimension}StageUpdated", EntityType.Order, orderId, beforeJson, afterJson, command.Reason, command.ClientRequestId);
                await RecordChangeAsync(connection, transaction, workspaceId, sequence, EntityType.Order, orderId, "stageUpdated", dto.Revision);

                return (dto, EntityType.Order, orderId);
            },
            cancellationToken);
    }

    public async Task<CommandResult<CloudOrderDto>> AddOrderNoteAsync(Guid workspaceId, Guid orderId, OrderNoteCommand command, CancellationToken cancellationToken = default)
    {
        var userId = _currentUser.UserId ?? throw new InvalidOperationException("User not authenticated.");
        var membership = await GetMembershipAsync(userId);

        if (string.IsNullOrWhiteSpace(command.Note))
            throw new InvalidOperationException("备注内容不能为空。");

        return await ExecuteWithIdempotencyAsync<OrderNoteCommand, CloudOrderDto>(
            workspaceId,
            "order:note",
            command,
            async (connection, transaction, sequence, ct) =>
            {
                await ThrowIfRevisionMismatchAsync(connection, transaction, "CommerceOrders", orderId, command.ExpectedRevision, ct);

                var before = await LoadOrderDtoAsync(connection, transaction, workspaceId, orderId, membership, ct);
                var beforeJson = await SnapshotJsonAsync(before);

                var customFields = ParseCustomFields(before.CustomFieldsJson);
                var notes = customFields.TryGetProperty("notes", out var notesElement)
                    ? notesElement.EnumerateArray()
                        .Select(e => e.GetString())
                        .Where(s => s != null)
                        .Cast<string>()
                        .ToList()
                    : new List<string>();
                notes.Add(command.Note.Trim());

                var updatedCustomFields = new Dictionary<string, object>(customFields.EnumerateObject().ToDictionary(p => p.Name, p => (object)p.Value.Clone()))
                {
                    ["notes"] = notes
                };
                var customFieldsJson = JsonSerializer.Serialize(updatedCustomFields, JsonOptions);

                await connection.ExecuteAsync(
                    @"UPDATE ""CommerceOrders""
                     SET ""CustomFieldsJson"" = @customFieldsJson,
                         ""Revision"" = ""Revision"" + 1,
                         ""UpdatedAt"" = @now,
                         ""UpdatedByUserId"" = @updatedBy,
                         ""LastChangeSequence"" = @sequence
                     WHERE ""Id"" = @orderId;",
                    new { customFieldsJson, now = DateTime.UtcNow, updatedBy = userId, sequence, orderId },
                    transaction);

                var dto = await LoadOrderDtoAsync(connection, transaction, workspaceId, orderId, membership, ct);
                var afterJson = await SnapshotJsonAsync(dto);
                await AuditAsync(connection, transaction, workspaceId, "OrderNoteAdded", EntityType.Order, orderId, beforeJson, afterJson, command.Reason, command.ClientRequestId);
                await RecordChangeAsync(connection, transaction, workspaceId, sequence, EntityType.Order, orderId, "noteAdded", dto.Revision);

                return (dto, EntityType.Order, orderId);
            },
            cancellationToken);
    }

    private async Task<CloudOrderDto> LoadOrderDtoAsync(IDbConnection connection, IDbTransaction transaction, Guid workspaceId, Guid orderId, CloudWorkspaceMemberRecord membership, CancellationToken cancellationToken)
    {
        var row = await connection.QueryFirstOrDefaultAsync(
            "SELECT * FROM \"CommerceOrders\" WHERE \"WorkspaceId\" = @workspaceId AND \"Id\" = @orderId;",
            new { workspaceId, orderId },
            transaction)
            ?? throw new InvalidOperationException($"订单 {orderId} 不存在。");

        return CommerceDtoMapper.ToOrderDto(row, _permissions.CanViewCosts(membership));
    }

    private async Task InsertOrderItemsAsync(IDbConnection connection, IDbTransaction transaction, Guid workspaceId, Guid orderId, List<CreateOrderItemCommand> items, long sequence, Guid userId, DateTime now, CancellationToken cancellationToken)
    {
        foreach (var item in items)
        {
            var lineTotal = RoundMoney(item.UnitPrice * item.Quantity);
            await connection.ExecuteAsync(
                @"INSERT INTO ""CommerceOrderItems"" (
                    ""Id"", ""WorkspaceId"", ""CreatedAt"", ""UpdatedAt"", ""DeletedAt"", ""Lifecycle"",
                    ""Revision"", ""CreatedByUserId"", ""UpdatedByUserId"", ""LastChangeSequence"",
                    ""OrderId"", ""ProductId"", ""ProductVariantId"", ""InventoryItemId"", ""UnitId"",
                    ""Description"", ""Quantity"", ""UnitPrice"", ""UnitCost"", ""LineTotal"")
                VALUES (
                    @id, @workspaceId, @now, @now, NULL, 0,
                    1, @createdBy, @updatedBy, @sequence,
                    @orderId, @productId, @productVariantId, @inventoryItemId, @unitId,
                    @description, @quantity, @unitPrice, @unitCost, @lineTotal);",
                new
                {
                    id = Guid.NewGuid(),
                    workspaceId,
                    now,
                    createdBy = userId,
                    updatedBy = userId,
                    sequence,
                    orderId,
                    item.ProductId,
                    item.ProductVariantId,
                    item.InventoryItemId,
                    item.UnitId,
                    item.Description,
                    quantity = RoundQuantity(item.Quantity),
                    unitPrice = RoundMoney(item.UnitPrice),
                    unitCost = item.UnitCost.HasValue ? RoundMoney(item.UnitCost.Value) : (decimal?)null,
                    lineTotal
                },
                transaction);
        }
    }

    private (decimal subtotal, decimal total, decimal? cost, decimal? grossProfit, decimal? grossMargin) ComputeOrderTotals(List<CreateOrderItemCommand> items)
    {
        decimal subtotal = 0m;
        decimal cost = 0m;
        foreach (var item in items)
        {
            var lineTotal = RoundMoney(item.UnitPrice * item.Quantity);
            subtotal += lineTotal;
            cost += item.UnitCost.HasValue ? RoundMoney(item.UnitCost.Value * item.Quantity) : 0m;
        }

        var total = subtotal;
        var grossProfit = total - cost;
        decimal? grossMargin = total > 0m ? Math.Round(grossProfit / total * 100m, 2, MidpointRounding.AwayFromZero) : null;

        return (subtotal, total, cost > 0m ? cost : null, grossProfit > 0m ? grossProfit : null, grossMargin);
    }

    private async Task RecomputeCustomerStatisticsAsync(IDbConnection connection, IDbTransaction transaction, Guid workspaceId, Guid customerId, long sequence, Guid userId, CancellationToken cancellationToken)
    {
        var completedOrderRows = await connection.QueryAsync(
            @"SELECT ""Total"", ""OrderedAt"" FROM ""CommerceOrders""
             WHERE ""WorkspaceId"" = @workspaceId AND ""CustomerId"" = @customerId
               AND ""SalesStage"" = @completed AND ""DeletedAt"" IS NULL;",
            new { workspaceId, customerId, completed = (int)OrderSalesStage.Completed },
            transaction);

        var completedOrders = completedOrderRows.ToList();
        var totalSpend = completedOrders.Sum(r => (decimal)r.Total);
        var lastOrderAt = completedOrders.Any() ? (DateTime?)completedOrders.Max(r => (DateTime)r.OrderedAt) : null;

        await connection.ExecuteAsync(
            @"UPDATE ""CommerceCustomers""
             SET ""CompletedOrderCount"" = @count,
                 ""TotalSpend"" = @totalSpend,
                 ""LastOrderAt"" = @lastOrderAt,
                 ""Revision"" = ""Revision"" + 1,
                 ""UpdatedAt"" = @now,
                 ""UpdatedByUserId"" = @updatedBy,
                 ""LastChangeSequence"" = @sequence
             WHERE ""Id"" = @customerId;",
            new
            {
                count = completedOrders.Count,
                totalSpend = RoundMoney(totalSpend),
                lastOrderAt,
                now = DateTime.UtcNow,
                updatedBy = userId,
                sequence,
                customerId
            },
            transaction);
    }
}
