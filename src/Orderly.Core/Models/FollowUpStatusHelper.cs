namespace Orderly.Core.Models;

public static class FollowUpStatusHelper
{
    public static bool CanTransition(FollowUpStatus status)
    {
        return status is FollowUpStatus.Pending or FollowUpStatus.InProgress or FollowUpStatus.Overdue;
    }

    public static bool IsOpen(FollowUp followUp)
    {
        return followUp.CompletedAt is null && CanTransition(followUp.Status);
    }

    public static bool IsScheduledOn(FollowUp followUp, DateTime date)
    {
        return CanTransition(followUp.Status) && followUp.ScheduledAt.Date == date.Date;
    }

    public static bool IsOverdue(FollowUp followUp, DateTime today)
    {
        return CanTransition(followUp.Status) && followUp.ScheduledAt.Date < today.Date;
    }
}
