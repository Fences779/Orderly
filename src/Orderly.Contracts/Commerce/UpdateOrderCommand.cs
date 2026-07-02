using Orderly.Core.Commerce;

namespace Orderly.Contracts.Commerce;

public sealed class UpdateOrderCommand : WriteCommandBase
{
    public Guid OrderId { get; set; }
    public Guid? CustomerId { get; set; }
    public OrderSalesStage? SalesStage { get; set; }
    public OrderPaymentStage? PaymentStage { get; set; }
    public OrderFulfillmentStage? FulfillmentStage { get; set; }
    public string? Note { get; set; }
    public Guid? AssignedToUserId { get; set; }
}
