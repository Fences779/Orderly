namespace Orderly.Core.Models;

public sealed class QuickAction
{
    public QuickActionType Type { get; set; }
    public string Label { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string DisabledReason { get; set; } = string.Empty;
}
