namespace Orderly.Core.Models;

public sealed class WorkbenchTask
{
    public string Id { get; set; } = string.Empty;
    public string DedupeKey { get; set; } = string.Empty;
    public WorkbenchTaskType Type { get; set; }
    public WorkbenchTaskPriority Priority { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public int? CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public int? OrderId { get; set; }
    public string OrderDisplay { get; set; } = string.Empty;
    public int? DealId { get; set; }
    public string RelatedEntityType { get; set; } = string.Empty;
    public int? RelatedEntityId { get; set; }
    public int? MessageId { get; set; }
    public int? AiSuggestionId { get; set; }
    public int? OcrResultId { get; set; }
    public int? FollowUpId { get; set; }
    public string TargetSection { get; set; } = string.Empty;
    public string ActionHint { get; set; } = string.Empty;
    public IReadOnlyList<QuickAction> QuickActions { get; set; } = [];
    public DateTime OccurredAt { get; set; }
    public PipelineStage? PipelineStage { get; set; }
    public long SortKey { get; set; }
}
