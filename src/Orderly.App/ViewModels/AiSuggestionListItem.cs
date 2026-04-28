using Orderly.Core.Models;

namespace Orderly.App.ViewModels;

public sealed class AiSuggestionListItem
{
    public AiSuggestionListItem(AiSuggestion suggestion)
    {
        Suggestion = suggestion;
    }

    public AiSuggestion Suggestion { get; }
    public int Id => Suggestion.Id;
    public AiSuggestionStatus Status => Suggestion.Status;
    public string SuggestionText => Suggestion.SuggestionText;
    public string Reason => Suggestion.Reason;
    public string CreatedAtText => Suggestion.CreatedAt.ToString("MM-dd HH:mm");
    public string StatusText => Suggestion.Status switch
    {
        AiSuggestionStatus.Draft => "待处理",
        AiSuggestionStatus.Accepted => "已接受",
        AiSuggestionStatus.Rejected => "已拒绝",
        AiSuggestionStatus.Sent => "已发送",
        _ => Suggestion.Status.ToString()
    };
    public bool CanReview => Suggestion.Status == AiSuggestionStatus.Draft;
}
