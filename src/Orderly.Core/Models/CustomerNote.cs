namespace Orderly.Core.Models;

public sealed class CustomerNote : EntityBase
{
    public int CustomerId { get; set; }
    public int? DealId { get; set; }
    public int? OrderId { get; set; }
    public NoteType Type { get; set; }
    public string Content { get; set; } = string.Empty;
    public bool IsPinned { get; set; }
    public Customer? Customer { get; set; }
}
