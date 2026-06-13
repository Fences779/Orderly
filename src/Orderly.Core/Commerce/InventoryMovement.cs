namespace Orderly.Core.Commerce;

/// <summary>
/// A recorded change to an inventory item's quantity, owned by a single workspace (Req 2.2).
/// Movements are append-only audit facts: the owning item, movement type, quantity, and the
/// moment it occurred are fixed at creation. The optional <see cref="BusinessKey"/> makes
/// service-generated movements idempotent so re-running order completion produces no duplicates
/// (Req 4.20, 18.6).
/// </summary>
public sealed class InventoryMovement : WorkspaceScopedEntity
{
    private string? _note;

    /// <summary>Identity of the affected <see cref="InventoryItem"/>. Fixed at creation.</summary>
    public Guid InventoryItemId { get; init; }

    /// <summary>How this movement changes the item quantity (Req 4.8). Fixed at creation.</summary>
    public InventoryMovementType MovementType { get; init; }

    /// <summary>The quantity moved. Fixed at creation.</summary>
    public decimal Quantity { get; init; }

    /// <summary>Optional supplier link for inbound movements. Fixed at creation.</summary>
    public Guid? SupplierId { get; init; }

    /// <summary>Optional originating order for deduction movements. Fixed at creation.</summary>
    public Guid? OrderId { get; init; }

    /// <summary>The UTC moment the movement occurred. Fixed at creation.</summary>
    public DateTime OccurredAt { get; init; }

    /// <summary>Stable business key used for idempotent generation by the service layer (Req 4.20, 18.6).</summary>
    public string? BusinessKey { get; init; }

    /// <summary>Optional free-text note describing the movement.</summary>
    public string? Note
    {
        get => _note;
        set { _note = value; MarkUpdated(); }
    }
}
