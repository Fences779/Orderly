using Orderly.Core.Models;

namespace Orderly.Data.Services;

internal static class ProjectionTargetSections
{
    public static string Customer => NavigationSemantics.GetTargetSectionValue(NavigationTargetSection.Customer);
    public static string Order => NavigationSemantics.GetTargetSectionValue(NavigationTargetSection.Order);
    public static string Conversation => NavigationSemantics.GetTargetSectionValue(NavigationTargetSection.Conversation);
    public static string AiSuggestion => NavigationSemantics.GetTargetSectionValue(NavigationTargetSection.AiSuggestion);
    public static string Ocr => NavigationSemantics.GetTargetSectionValue(NavigationTargetSection.Ocr);
    public static string FollowUp => NavigationSemantics.GetTargetSectionValue(NavigationTargetSection.FollowUp);
    public static string ActivityLog => NavigationSemantics.GetTargetSectionValue(NavigationTargetSection.ActivityLog);
}
