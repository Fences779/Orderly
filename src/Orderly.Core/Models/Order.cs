namespace Orderly.Core.Models;

public class Order : EntityBase
{
    public int CustomerId { get; set; }
    public int? DealId { get; set; }
    public string Title { get; set; } = string.Empty;
    public OrderStatus Status { get; set; }
    public decimal Amount { get; set; }
    public string Requirement { get; set; } = string.Empty;
    public string SourcePlatform { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string RawPayload { get; set; } = string.Empty;
    public DateTime? NextFollowUpAt { get; set; }
    public Customer? Customer { get; set; }
}
