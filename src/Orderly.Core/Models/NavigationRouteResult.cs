namespace Orderly.Core.Models;

public sealed class NavigationRouteResult
{
    public bool CanNavigate { get; init; }
    public bool RequiresUserAction { get; init; }
    public bool UsedFallback { get; init; }
    public string DisabledReason { get; init; } = string.Empty;
    public string StatusMessage { get; init; } = string.Empty;
    public NavigationTarget? RequestedTarget { get; init; }
    public NavigationTarget? ResolvedTarget { get; init; }
}
