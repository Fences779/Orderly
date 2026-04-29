namespace Orderly.Core.Models;

public sealed class NavigationTarget
{
    public NavigationTargetSection TargetSection { get; init; }
    public NavigationActionHint ActionHint { get; init; }
    public int? CustomerId { get; init; }
    public int? OrderId { get; init; }
    public string RelatedEntityType { get; init; } = string.Empty;
    public int? RelatedEntityId { get; init; }
    public bool RequiresUserAction { get; init; }

    public string TargetSectionName => NavigationSemantics.GetTargetSectionValue(TargetSection);
    public string ActionHintName => NavigationSemantics.GetActionHintValue(ActionHint);
}
