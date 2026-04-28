namespace Orderly.Core.Models;

public sealed class AiSuggestion : EntityBase
{
    public int CustomerId { get; set; }
    public int? OrderId { get; set; }
    public int? MessageId { get; set; }
    public string SuggestionText { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public double? Confidence { get; set; }
    public AiSuggestionStatus Status { get; set; } = AiSuggestionStatus.Draft;
    public string MetadataJson { get; set; } = string.Empty;
}
