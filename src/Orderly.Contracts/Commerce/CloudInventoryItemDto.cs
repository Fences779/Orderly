namespace Orderly.Contracts.Commerce;

public sealed class CloudInventoryItemDto : CloudEntityDto
{
    public Guid WorkspaceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? ProductVariantId { get; set; }
    public Guid? UnitId { get; set; }
    public decimal QuantityAvailable { get; set; }
    public decimal ReorderThreshold { get; set; }
    public decimal? UnitCost { get; set; }
}
