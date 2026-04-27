namespace Orderly.Core.Models;

public sealed class PriceAdjustment : EntityBase
{
    public int CustomerId { get; set; }
    public int? DealId { get; set; }
    public int? OrderId { get; set; }
    public decimal OriginalAmount { get; set; }
    public decimal AdjustedAmount { get; set; }
    public string Reason { get; set; } = string.Empty;
    public PriceAdjustmentStatus Status { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public string ApprovedBy { get; set; } = string.Empty;
    public DateTime? ApprovedAt { get; set; }
}
