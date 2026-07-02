namespace Orderly.Contracts.Commerce;

public sealed class CloudOrderItemDto : CloudEntityDto
{
    public Guid WorkspaceId { get; set; }
    public Guid OrderId { get; set; }
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
