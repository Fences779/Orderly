using System.Text.Json;
using Orderly.Contracts.Commerce;
using Orderly.Contracts.Offline;
using Orderly.Contracts.Permissions;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Services;
using Orderly.Remote.Auth;
using Orderly.Remote.Clients;

namespace Orderly.Remote.Services;

public sealed class RemoteOrderService : IOrderService
{
    private readonly RemoteCommerceClient _client;
    private readonly CloudAuthSession _session;
    private readonly IEmergencyDraftQueue? _offlineDraftQueue;

    public RemoteOrderService(RemoteCommerceClient client, CloudAuthSession session, IEmergencyDraftQueue? offlineDraftQueue = null)
    {
        _client = client;
        _session = session;
        _offlineDraftQueue = offlineDraftQueue;
    }

    public void RecalculateOrder(Order order, IReadOnlyCollection<OrderItem> items, IReadOnlyCollection<PaymentRecord> payments)
    {
        var lineTotal = items.Sum(i => i.LineTotal.Amount);
        var cost = items.Sum(i => i.UnitCost.Amount * i.Quantity);
        order.Subtotal = CommerceMoney.From(lineTotal);
        order.Total = CommerceMoney.From(lineTotal);
        order.Cost = CommerceMoney.From(cost);
        order.GrossProfit = CommerceMoney.From(lineTotal - cost);
        order.GrossMargin = lineTotal != 0m ? Math.Round((lineTotal - cost) / lineTotal * 100m, 2) : 0m;
        order.PaidAmount = CommerceMoney.From(payments.Sum(p => p.Amount.Amount));
        order.ReceivableAmount = CommerceMoney.From(order.Total.Amount - order.PaidAmount.Amount);
    }

    public OrderStageTransitionResult ApplyStageTransition(Order order, OrderStageTransitionRequest request, OrderWorkflowConfiguration workflow)
    {
        if (request.TargetSalesStage.HasValue)
            order.SalesStage = request.TargetSalesStage.Value;
        if (request.TargetPaymentStage.HasValue)
            order.PaymentStage = request.TargetPaymentStage.Value;
        if (request.TargetFulfillmentStage.HasValue)
            order.FulfillmentStage = request.TargetFulfillmentStage.Value;
        return OrderStageTransitionResult.Applied();
    }

    public async Task<OrderCompletionResult> CompleteOrderAsync(Guid orderId, DateTime completedAtUtc, CancellationToken cancellationToken = default)
    {
        var latest = await _client.GetAsync<CloudOrderDto>($"api/workspaces/{_session.WorkspaceId:N}/orders/{orderId:N}", cancellationToken)
            .ConfigureAwait(false);

        var command = new CompleteOrderCommand
        {
            ClientRequestId = Guid.NewGuid().ToString("N"),
            ExpectedRevision = latest?.Revision ?? 0L,
            CompletedAtUtc = completedAtUtc
        };

        try
        {
            await _client.PostAsync<CompleteOrderCommand, CloudOrderDto>(
                $"api/workspaces/{_session.WorkspaceId:N}/orders/{orderId:N}/complete",
                command,
                cancellationToken).ConfigureAwait(false);
            return OrderCompletionResult.Completed();
        }
        catch (RemoteConflictException ex)
        {
            return OrderCompletionResult.InsufficientInventory(
                Array.Empty<InventoryShortfall>(),
                $"远程服务器拒绝完成订单：{ex.Message}");
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("当前离线，订单完成不支持离线保存，请恢复网络后重试。");
        }
    }

    public async Task AddNoteAsync(Guid orderId, string note, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(note))
            throw new ArgumentException("备注内容不能为空。", nameof(note));

        var latest = await _client.GetAsync<CloudOrderDto>(
            $"api/workspaces/{_session.WorkspaceId:N}/orders/{orderId:N}",
            cancellationToken).ConfigureAwait(false);

        var command = new OrderNoteCommand
        {
            ClientRequestId = Guid.NewGuid().ToString("N"),
            ExpectedRevision = latest?.Revision ?? 0L,
            OrderId = orderId,
            Note = note.Trim()
        };

        try
        {
            await _client.PostAsync<OrderNoteCommand, CloudOrderDto>(
                $"api/workspaces/{_session.WorkspaceId:N}/orders/{orderId:N}/notes",
                command,
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException) when (_offlineDraftQueue is not null)
        {
            await _offlineDraftQueue.AddAsync(new EmergencyDraftDto
            {
                Id = Guid.NewGuid().ToString("N"),
                EntityType = EntityType.Order,
                EntityId = orderId.ToString("N"),
                OperationType = "note",
                PayloadJson = JsonSerializer.Serialize(command),
                BaseRevision = command.ExpectedRevision,
                CreatedAtUtc = DateTime.UtcNow,
                Status = "Pending"
            }, cancellationToken).ConfigureAwait(false);

            throw new InvalidOperationException("当前离线，订单备注已保存为应急草稿。联网后请到设置页确认提交。");
        }
    }

    public async Task UpdateStageAsync(Guid orderId, OrderStageTransitionRequest request, CancellationToken cancellationToken = default)
    {
        if (request.NamedDimensionCount != 1)
            throw new ArgumentException("每次只能更新一个订单阶段维度。", nameof(request));

        var latest = await _client.GetAsync<CloudOrderDto>(
            $"api/workspaces/{_session.WorkspaceId:N}/orders/{orderId:N}",
            cancellationToken).ConfigureAwait(false);

        var command = new OrderStageCommand
        {
            ClientRequestId = Guid.NewGuid().ToString("N"),
            ExpectedRevision = latest?.Revision ?? 0L,
            TargetSalesStage = request.TargetSalesStage,
            TargetPaymentStage = request.TargetPaymentStage,
            TargetFulfillmentStage = request.TargetFulfillmentStage
        };

        var pathSegment = InferDimension(request) switch
        {
            "sales" => "stage",
            "payment" => "payment-status",
            "fulfillment" => "fulfillment-status",
            _ => throw new InvalidOperationException("未识别的阶段维度。")
        };

        try
        {
            await _client.PostAsync<OrderStageCommand, CloudOrderDto>(
                $"api/workspaces/{_session.WorkspaceId:N}/orders/{orderId:N}/{pathSegment}",
                command,
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException) when (_offlineDraftQueue is not null)
        {
            await _offlineDraftQueue.AddAsync(new EmergencyDraftDto
            {
                Id = Guid.NewGuid().ToString("N"),
                EntityType = EntityType.Order,
                EntityId = orderId.ToString("N"),
                OperationType = "stage",
                PayloadJson = JsonSerializer.Serialize(command),
                BaseRevision = command.ExpectedRevision,
                CreatedAtUtc = DateTime.UtcNow,
                Status = "Pending"
            }, cancellationToken).ConfigureAwait(false);

            throw new InvalidOperationException("当前离线，订单阶段已保存为应急草稿。联网后请到设置页确认提交。");
        }
    }

    private static string InferDimension(OrderStageTransitionRequest request)
    {
        if (request.TargetSalesStage.HasValue) return "sales";
        if (request.TargetPaymentStage.HasValue) return "payment";
        if (request.TargetFulfillmentStage.HasValue) return "fulfillment";
        throw new InvalidOperationException("No stage dimension specified.");
    }
}
