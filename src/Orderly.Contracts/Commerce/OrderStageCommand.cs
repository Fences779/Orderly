using Orderly.Core.Commerce;

namespace Orderly.Contracts.Commerce;

public sealed class OrderStageCommand : WriteCommandBase
{
    public OrderSalesStage? TargetSalesStage { get; set; }
    public OrderPaymentStage? TargetPaymentStage { get; set; }
    public OrderFulfillmentStage? TargetFulfillmentStage { get; set; }
}
