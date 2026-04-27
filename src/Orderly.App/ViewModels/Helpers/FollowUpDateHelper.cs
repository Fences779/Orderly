using Orderly.Core.Models;

namespace Orderly.App.ViewModels.Helpers;

internal static class FollowUpDateHelper
{
    public static bool CanTransitionFollowUp(FollowUpStatus status)
    {
        return status is FollowUpStatus.Pending or FollowUpStatus.InProgress or FollowUpStatus.Overdue;
    }

    public static bool IsPendingOrder(OrderStatus status)
    {
        return status is OrderStatus.PendingCommunication or OrderStatus.PendingQuote or OrderStatus.PendingFollowUp;
    }

    public static bool IsOrderFollowUpOn(Order order, DateTime date)
    {
        return order.NextFollowUpAt?.Date == date.Date;
    }

    public static bool IsOrderFollowUpOverdue(Order order, DateTime today)
    {
        return order.NextFollowUpAt?.Date < today.Date && IsPendingOrder(order.Status);
    }
}
