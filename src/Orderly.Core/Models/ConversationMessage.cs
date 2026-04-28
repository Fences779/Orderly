namespace Orderly.Core.Models;

public sealed class ConversationMessage : EntityBase
{
    public int CustomerId { get; set; }
    public int? OrderId { get; set; }
    public int? DealId { get; set; }
    public MessageDirection Direction { get; set; }
    public MessageChannel Channel { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime MessageTime { get; set; } = DateTime.Now;
    public string SourceMessageId { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = string.Empty;
}
