namespace Orderly.Core.Commerce;

/// <summary>
/// A line on an <see cref="Order"/>, owned by a single workspace (Req 2.2). The owning
/// <see cref="OrderId"/> is fixed at creation. The <see cref="InventoryItemId"/> link is
/// <b>optional</b> (Req 4.6): a line linked to an inventory item participates in the aggregated
/// availability check and deduction at completion, while a non-linked line (a service, a custom
/// item, or a product not stocked) neither blocks completion nor incurs deduction. Mutable fields
/// advance <see cref="CommerceEntity.UpdatedAt"/> when changed (Req 2.8).
/// </summary>
public sealed class OrderItem : WorkspaceScopedEntity
{
    private Guid? _productId;
    private Guid? _productVariantId;
    private Guid? _inventoryItemId;
    private Guid? _unitId;
    private string? _description;
    private decimal _quantity;
    private CommerceMoney _unitPrice = CommerceMoney.Zero;
    private CommerceMoney _unitCost = CommerceMoney.Zero;
    private CommerceMoney _lineTotal = CommerceMoney.Zero;

    /// <summary>Identity of the owning <see cref="Order"/>. Fixed at creation.</summary>
    public Guid OrderId { get; init; }

    /// <summary>Optional link to the ordered <see cref="Product"/>.</summary>
    public Guid? ProductId
    {
        get => _productId;
        set { _productId = value; MarkUpdated(); }
    }

    /// <summary>Optional link to a specific <see cref="ProductVariant"/>.</summary>
    public Guid? ProductVariantId
    {
        get => _productVariantId;
        set { _productVariantId = value; MarkUpdated(); }
    }

    /// <summary>
    /// Optional link to the <see cref="InventoryItem"/> this line draws from. When null, the line
    /// does not participate in inventory availability checks or deductions (Req 4.6).
    /// </summary>
    public Guid? InventoryItemId
    {
        get => _inventoryItemId;
        set { _inventoryItemId = value; MarkUpdated(); }
    }

    /// <summary>Optional unit (<see cref="UnitDefinition"/>) the quantity is expressed in.</summary>
    public Guid? UnitId
    {
        get => _unitId;
        set { _unitId = value; MarkUpdated(); }
    }

    /// <summary>Optional free-text description for the line.</summary>
    public string? Description
    {
        get => _description;
        set { _description = value; MarkUpdated(); }
    }

    /// <summary>Quantity ordered on this line; aggregated per <see cref="InventoryItemId"/> at completion (Req 4.16).</summary>
    public decimal Quantity
    {
        get => _quantity;
        set { _quantity = value; MarkUpdated(); }
    }

    /// <summary>Unit selling price.</summary>
    public CommerceMoney UnitPrice
    {
        get => _unitPrice;
        set { _unitPrice = value; MarkUpdated(); }
    }

    /// <summary>Unit cost.</summary>
    public CommerceMoney UnitCost
    {
        get => _unitCost;
        set { _unitCost = value; MarkUpdated(); }
    }

    /// <summary>Computed line total (price × quantity, after line adjustments). Monetary, scale 2.</summary>
    public CommerceMoney LineTotal
    {
        get => _lineTotal;
        set { _lineTotal = value; MarkUpdated(); }
    }
}
