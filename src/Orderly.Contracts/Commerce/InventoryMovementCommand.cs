using Orderly.Core.Commerce;

namespace Orderly.Contracts.Commerce;

public sealed class InventoryMovementCommand : WriteCommandBase
{
    public Guid InventoryItemId { get; set; }
    public InventoryMovementType MovementType { get; set; }
    public decimal Quantity { get; set; }
    public Guid? SupplierId { get; set; }
    public Guid? OrderId { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? BusinessKey { get; set; }
    public string? Note { get; set; }
    public bool IsStocktake { get; set; }
}
