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
        catch (HttpRequestException) when (_offlineDraftQueue is not null)
        {
            await _offlineDraftQueue.AddAsync(new EmergencyDraftDto
            {
                Id = Guid.NewGuid().ToString("N"),
                EntityType = EntityType.Order,
                EntityId = orderId.ToString("N"),
                OperationType = "complete",
                PayloadJson = JsonSerializer.Serialize(command),
                BaseRevision = command.ExpectedRevision,
                CreatedAtUtc = DateTime.UtcNow,
                Status = "Pending"
            }, cancellationToken).ConfigureAwait(false);

            return OrderCompletionResult.Completed("当前离线，订单完成已保存为应急草稿，联网后自动提交。");
        }
    }
}
