namespace Orderly.Core.Models;

public sealed class QuickAction
{
    public QuickActionType Type { get; set; }
    public string Label { get; set; } = string.Empty;
    public string TargetSection { get; set; } = string.Empty;
    public string ActionHint { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string DisabledReason { get; set; } = string.Empty;
    public int? CustomerId { get; set; }
    public int? OrderId { get; set; }
    public string RelatedEntityType { get; set; } = string.Empty;
    public int? RelatedEntityId { get; set; }
    public bool RequiresUserAction { get; set; }
}
