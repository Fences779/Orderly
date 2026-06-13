namespace Orderly.Core.Commerce;

/// <summary>
/// Industry-agnostic classification of a product.
/// </summary>
public enum ProductType
{
    /// <summary>A tangible item that can be stocked in inventory.</summary>
    Physical = 0,

    /// <summary>A service that is performed rather than stocked.</summary>
    Service = 1,

    /// <summary>A digital good delivered electronically.</summary>
    Digital = 2,

    /// <summary>A bundle composed of other products.</summary>
    Bundle = 3
}
