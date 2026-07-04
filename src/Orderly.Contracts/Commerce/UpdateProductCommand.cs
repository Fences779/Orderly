using Orderly.Core.Commerce;

namespace Orderly.Contracts.Commerce;

public sealed class UpdateProductCommand : WriteCommandBase
{
    public Guid ProductId { get; set; }
    public string? Name { get; set; }
    public string? Code { get; set; }
    public ProductType? ProductType { get; set; }
    public string? Description { get; set; }
    public Guid? DefaultUnitId { get; set; }
    public Guid? SupplierId { get; set; }
    public decimal? DefaultPrice { get; set; }
    public decimal? DefaultCost { get; set; }
}
