namespace Orderly.Core.Models;

public sealed class SearchResultItem
{
    public string Id { get; set; } = string.Empty;
    public SearchResultType Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public int? CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public string RelatedEntityType { get; set; } = string.Empty;
    public int? RelatedEntityId { get; set; }
    public DateTime OccurredAt { get; set; }
    public string MatchedField { get; set; } = string.Empty;
    public int Score { get; set; }
    public SearchResultPriority Priority { get; set; }
    public PipelineStage? PipelineStage { get; set; }
    public string TargetSection { get; set; } = string.Empty;
    public string ActionHint { get; set; } = string.Empty;
}
