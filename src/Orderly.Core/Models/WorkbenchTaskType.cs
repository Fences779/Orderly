namespace Orderly.Core.Models;

public enum WorkbenchTaskType
{
    ReplyNeeded = 0,
    DraftNotSent = 1,
    AiSuggestionPending = 2,
    OcrNotConverted = 3,
    FollowUpToday = 4,
    FollowUpOverdue = 5,
    RecentlyActiveCustomer = 6
}
