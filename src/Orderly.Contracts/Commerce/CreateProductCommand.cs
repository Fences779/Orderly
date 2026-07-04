using Orderly.Core.Commerce;

namespace Orderly.Contracts.Commerce;

public sealed class CreateProductCommand : WriteCommandBase
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public ProductType ProductType { get; set; }
    public string? Description { get; set; }
    public Guid? DefaultUnitId { get; set; }
    public Guid? SupplierId { get; set; }
    public decimal DefaultPrice { get; set; }
    public decimal? DefaultCost { get; set; }
}
