namespace Orderly.Core.Models;

public sealed class WorkbenchTaskFilter
{
    public WorkbenchTaskType? TaskType { get; set; }
    public WorkbenchTaskPriority? Priority { get; set; }
    public PipelineStage? PipelineStage { get; set; }
    public int? CustomerId { get; set; }
    public int? OrderId { get; set; }
    public string TargetSection { get; set; } = string.Empty;
    public bool? OnlyActionable { get; set; }
    public bool? IncludeRecentlyActive { get; set; }
    public DateTime? OccurredFrom { get; set; }
    public DateTime? OccurredTo { get; set; }
}
