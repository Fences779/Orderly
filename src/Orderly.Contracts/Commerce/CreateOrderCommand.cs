using Orderly.Core.Commerce;

namespace Orderly.Contracts.Commerce;

public sealed class CreateOrderCommand : WriteCommandBase
{
    public string OrderNo { get; set; } = string.Empty;
    public Guid? CustomerId { get; set; }
    public OrderSalesStage SalesStage { get; set; }
    public OrderPaymentStage PaymentStage { get; set; }
    public OrderFulfillmentStage FulfillmentStage { get; set; }
    public DateTime OrderedAtUtc { get; set; }
    public string? Note { get; set; }
    public List<CreateOrderItemCommand> Items { get; set; } = new();
}
