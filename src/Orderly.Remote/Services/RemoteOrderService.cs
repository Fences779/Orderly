using Orderly.Contracts.Commerce;
using Orderly.Core.Commerce;
using Orderly.Core.Commerce.Services;
using Orderly.Remote.Auth;
using Orderly.Remote.Clients;

namespace Orderly.Remote.Services;

public sealed class RemoteOrderService : IOrderService
{
    private readonly RemoteCommerceClient _client;
    private readonly CloudAuthSession _session;

    public RemoteOrderService(RemoteCommerceClient client, CloudAuthSession session)
    {
        _client = client;
        _session = session;
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
        var command = new CompleteOrderCommand { CompletedAtUtc = completedAtUtc };
        await _client.PostAsync<CompleteOrderCommand, CloudOrderDto>($"api/workspaces/{_session.WorkspaceId:N}/orders/{orderId:N}/complete", command, cancellationToken);
        return OrderCompletionResult.Completed();
    }
}
