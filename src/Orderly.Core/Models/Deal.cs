namespace Orderly.Core.Models;

public sealed class Deal : EntityBase
{
    public int CustomerId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DealStage Stage { get; set; }
    public decimal EstimatedAmount { get; set; }
    public string Requirement { get; set; } = string.Empty;
    public string SourcePlatform { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public DateTime? ExpectedCloseAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string LostReason { get; set; } = string.Empty;
    public Customer? Customer { get; set; }
}
