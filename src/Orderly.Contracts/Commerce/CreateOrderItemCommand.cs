namespace Orderly.Contracts.Commerce;

public sealed class CreateOrderItemCommand
{
    public Guid? ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public Guid? InventoryItemId { get; set; }
    public Guid? UnitId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal? UnitCost { get; set; }
}
