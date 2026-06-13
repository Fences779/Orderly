namespace Orderly.Core.Commerce;

/// <summary>
/// A stocked inventory item owned by a single workspace (Req 2.2). Holds the
/// <see cref="QuantityAvailable"/> against which order-completion availability checks run and the
/// <see cref="ReorderThreshold"/> that drives low-stock detection (Req 4.9). Quantity and
/// threshold are mutated by the inventory and order services, so they route through the base
/// touch mechanism to advance <see cref="CommerceEntity.UpdatedAt"/> (Req 2.8).
/// </summary>
public sealed class InventoryItem : WorkspaceScopedEntity
{
    private string _name = string.Empty;
    private string? _sku;
    private Guid? _productId;
    private Guid? _productVariantId;
    private Guid? _unitId;
    private decimal _quantityAvailable;
    private decimal _reorderThreshold;
    private CommerceMoney _unitCost = CommerceMoney.Zero;

    /// <summary>Display name of the inventory item.</summary>
    public string Name
    {
        get => _name;
        set { _name = value; MarkUpdated(); }
    }

    /// <summary>Optional stock-keeping code used as the primary import match key (design Import section).</summary>
    public string? Sku
    {
        get => _sku;
        set { _sku = value; MarkUpdated(); }
    }

    /// <summary>Optional link to the <see cref="Product"/> this item stocks.</summary>
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

    /// <summary>Optional unit (<see cref="UnitDefinition"/>) the quantity is expressed in.</summary>
    public Guid? UnitId
    {
        get => _unitId;
        set { _unitId = value; MarkUpdated(); }
    }

    /// <summary>Available on-hand quantity used for availability checks and deductions (Req 4.6, 4.7).</summary>
    public decimal QuantityAvailable
    {
        get => _quantityAvailable;
        set { _quantityAvailable = value; MarkUpdated(); }
    }

    /// <summary>Reorder threshold; low-stock is true when available quantity ≤ this value (Req 4.9).</summary>
    public decimal ReorderThreshold
    {
        get => _reorderThreshold;
        set { _reorderThreshold = value; MarkUpdated(); }
    }

    /// <summary>Cost per unit of the stocked item.</summary>
    public CommerceMoney UnitCost
    {
        get => _unitCost;
        set { _unitCost = value; MarkUpdated(); }
    }
}
