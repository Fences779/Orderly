namespace Orderly.Core.Commerce;

/// <summary>
/// A sellable product owned by a single workspace (Req 2.2). Industry-agnostic: variant- and
/// industry-specific attributes live in <see cref="CommerceEntity.CustomFieldsJson"/> rather than
/// as top-level fields (Req 2.3, 2.4). Mutable fields route through the base touch mechanism so
/// <see cref="CommerceEntity.UpdatedAt"/> advances on every change (Req 2.8).
/// </summary>
public sealed class Product : WorkspaceScopedEntity
{
    private string _name = string.Empty;
    private string? _code;
    private ProductType _productType = ProductType.Physical;
    private string? _description;
    private Guid? _defaultUnitId;
    private Guid? _supplierId;
    private CommerceMoney _defaultPrice = CommerceMoney.Zero;
    private CommerceMoney _defaultCost = CommerceMoney.Zero;

    /// <summary>Display name of the product.</summary>
    public string Name
    {
        get => _name;
        set { _name = value; MarkUpdated(); }
    }

    /// <summary>Optional business code/SKU used as the primary import match key (design Import section).</summary>
    public string? Code
    {
        get => _code;
        set { _code = value; MarkUpdated(); }
    }

    /// <summary>Industry-agnostic product classification.</summary>
    public ProductType ProductType
    {
        get => _productType;
        set { _productType = value; MarkUpdated(); }
    }

    /// <summary>Optional free-text description.</summary>
    public string? Description
    {
        get => _description;
        set { _description = value; MarkUpdated(); }
    }

    /// <summary>Optional default unit (<see cref="UnitDefinition"/>) for the product.</summary>
    public Guid? DefaultUnitId
    {
        get => _defaultUnitId;
        set { _defaultUnitId = value; MarkUpdated(); }
    }

    /// <summary>Optional default supplier link.</summary>
    public Guid? SupplierId
    {
        get => _supplierId;
        set { _supplierId = value; MarkUpdated(); }
    }

    /// <summary>Default selling price.</summary>
    public CommerceMoney DefaultPrice
    {
        get => _defaultPrice;
        set { _defaultPrice = value; MarkUpdated(); }
    }

    /// <summary>Default unit cost.</summary>
    public CommerceMoney DefaultCost
    {
        get => _defaultCost;
        set { _defaultCost = value; MarkUpdated(); }
    }
}
