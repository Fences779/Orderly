namespace Orderly.Core.Commerce;

/// <summary>
/// A variant of a <see cref="Product"/> (for example a size or option), owned by a single
/// workspace (Req 2.2). The owning <see cref="ProductId"/> is fixed at creation; descriptive and
/// pricing fields are mutable and advance <see cref="CommerceEntity.UpdatedAt"/> when changed
/// (Req 2.8). Variant-specific attributes beyond these neutral fields live in
/// <see cref="CommerceEntity.CustomFieldsJson"/> (Req 2.4).
/// </summary>
public sealed class ProductVariant : WorkspaceScopedEntity
{
    private string _name = string.Empty;
    private string? _sku;
    private CommerceMoney _priceAdjustment = CommerceMoney.Zero;

    /// <summary>Identity of the parent <see cref="Product"/>. Fixed at creation.</summary>
    public Guid ProductId { get; init; }

    /// <summary>Display name of the variant.</summary>
    public string Name
    {
        get => _name;
        set { _name = value; MarkUpdated(); }
    }

    /// <summary>Optional stock-keeping code for the variant.</summary>
    public string? Sku
    {
        get => _sku;
        set { _sku = value; MarkUpdated(); }
    }

    /// <summary>Price adjustment applied on top of the parent product's default price.</summary>
    public CommerceMoney PriceAdjustment
    {
        get => _priceAdjustment;
        set { _priceAdjustment = value; MarkUpdated(); }
    }
}
