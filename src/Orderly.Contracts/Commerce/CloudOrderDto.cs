using Orderly.Core.Commerce;

namespace Orderly.Contracts.Commerce;

public sealed class CloudOrderDto : CloudEntityDto
{
    public Guid WorkspaceId { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public Guid? CustomerId { get; set; }
    public OrderSalesStage SalesStage { get; set; }
    public OrderPaymentStage PaymentStage { get; set; }
    public OrderFulfillmentStage FulfillmentStage { get; set; }
    public decimal Subtotal { get; set; }
    public decimal Total { get; set; }
    public decimal? Cost { get; set; }
    public decimal? GrossProfit { get; set; }
    public decimal? GrossMargin { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal ReceivableAmount { get; set; }
    public DateTime OrderedAtUtc { get; set; }
    public string? Note { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public Guid? ArchivedByUserId { get; set; }
    public string? ArchiveReason { get; set; }
}
