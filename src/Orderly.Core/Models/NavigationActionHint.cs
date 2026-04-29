namespace Orderly.Core.Models;

public enum NavigationActionHint
{
    None = 0,
    OpenCustomer = 1,
    OpenOrder = 2,
    ReplyToCustomer = 3,
    ReviewSuggestion = 4,
    ReviewDraft = 5,
    CopyDraft = 6,
    MarkSent = 7,
    ConvertOcrToMessage = 8,
    CompleteFollowUp = 9,
    SnoozeFollowUp = 10
}
