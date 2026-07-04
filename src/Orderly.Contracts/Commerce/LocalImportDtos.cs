using Orderly.Core.Commerce;

namespace Orderly.Contracts.Commerce;

public sealed class LocalImportPackage
{
    public List<LocalProductDto> Products { get; set; } = new();
    public List<LocalCustomerDto> Customers { get; set; } = new();
    public List<LocalInventoryItemDto> InventoryItems { get; set; } = new();
    public List<LocalOrderDto> Orders { get; set; } = new();
    public List<LocalOrderItemDto> OrderItems { get; set; } = new();
    public List<LocalPaymentRecordDto> PaymentRecords { get; set; } = new();
    public List<LocalCashFlowEntryDto> CashFlowEntries { get; set; } = new();
}

public sealed class LocalProductDto
{
    public string SourceLocalEntityId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public ProductType ProductType { get; set; }
    public string? Description { get; set; }
    public Guid? DefaultUnitId { get; set; }
    public Guid? SupplierId { get; set; }
    public decimal DefaultPrice { get; set; }
    public decimal? DefaultCost { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class LocalCustomerDto
{
    public string SourceLocalEntityId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? WeChat { get; set; }
    public string? Email { get; set; }
    public DateTime? LastOrderAtUtc { get; set; }
    public int CompletedOrderCount { get; set; }
    public decimal TotalSpend { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class LocalInventoryItemDto
{
    public string SourceLocalEntityId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string? SourceProductLocalEntityId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public Guid? UnitId { get; set; }
    public decimal QuantityAvailable { get; set; }
    public decimal ReorderThreshold { get; set; }
    public decimal? UnitCost { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class LocalOrderDto
{
    public string SourceLocalEntityId { get; set; } = string.Empty;
    public string OrderNo { get; set; } = string.Empty;
    public string? SourceCustomerLocalEntityId { get; set; }
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
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class LocalOrderItemDto
{
    public string SourceLocalEntityId { get; set; } = string.Empty;
    public string SourceOrderLocalEntityId { get; set; } = string.Empty;
    public string? SourceProductLocalEntityId { get; set; }
    public string? SourceInventoryItemLocalEntityId { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public Guid? InventoryItemId { get; set; }
    public Guid? UnitId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal? UnitCost { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class LocalPaymentRecordDto
{
    public string SourceLocalEntityId { get; set; } = string.Empty;
    public string? SourceOrderLocalEntityId { get; set; }
    public string? SourceCashFlowEntryLocalEntityId { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? CashFlowEntryId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaidAtUtc { get; set; }
    public int Method { get; set; }
    public string? BusinessKey { get; set; }
}

public sealed class LocalCashFlowEntryDto
{
    public string SourceLocalEntityId { get; set; } = string.Empty;
    public CashFlowDirection Direction { get; set; }
    public decimal Amount { get; set; }
    public decimal SettledAmount { get; set; }
    public CashFlowSettlementStatus SettlementStatus { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public DateTime? DueDateUtc { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string? SourceOrderLocalEntityId { get; set; }
    public Guid? OrderId { get; set; }
    public string? BusinessKey { get; set; }
}
