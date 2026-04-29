using Orderly.Core.Models;

namespace Orderly.Data.Services;

internal static class ProjectionActionHints
{
    public static string OpenCustomer => NavigationSemantics.GetActionHintValue(NavigationActionHint.OpenCustomer);
    public static string OpenOrder => NavigationSemantics.GetActionHintValue(NavigationActionHint.OpenOrder);
    public static string ReviewSuggestion => NavigationSemantics.GetActionHintValue(NavigationActionHint.ReviewSuggestion);
    public static string ReviewDraft => NavigationSemantics.GetActionHintValue(NavigationActionHint.ReviewDraft);
    public static string CopyDraft => NavigationSemantics.GetActionHintValue(NavigationActionHint.CopyDraft);
    public static string MarkSent => NavigationSemantics.GetActionHintValue(NavigationActionHint.MarkSent);
    public static string ConvertOcrToMessage => NavigationSemantics.GetActionHintValue(NavigationActionHint.ConvertOcrToMessage);
    public static string ConvertOcr => ConvertOcrToMessage;
    public static string CompleteFollowUp => NavigationSemantics.GetActionHintValue(NavigationActionHint.CompleteFollowUp);
    public static string SnoozeFollowUp => NavigationSemantics.GetActionHintValue(NavigationActionHint.SnoozeFollowUp);
    public static string ReplyToCustomer => NavigationSemantics.GetActionHintValue(NavigationActionHint.ReplyToCustomer);
}
