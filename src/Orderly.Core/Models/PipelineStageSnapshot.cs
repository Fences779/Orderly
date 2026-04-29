namespace Orderly.Core.Models;

public sealed class PipelineStageSnapshot
{
    public int CustomerId { get; set; }
    public int? OrderId { get; set; }
    public int? DealId { get; set; }
    public PipelineStage Stage { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool UsedFallback { get; set; }
    public DateTime ResolvedAt { get; set; } = DateTime.Now;
}
