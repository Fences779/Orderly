namespace Orderly.Core.Models;

public sealed class FollowUp : EntityBase
{
    public int CustomerId { get; set; }
    public int? DealId { get; set; }
    public int? OrderId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public FollowUpStatus Status { get; set; }
    public DateTime ScheduledAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }
    public DateTime? ReminderAt { get; set; }
    public Customer? Customer { get; set; }
}
