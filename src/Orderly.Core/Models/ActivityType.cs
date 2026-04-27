namespace Orderly.Core.Models;

public enum ActivityType
{
    CustomerCreated = 0,
    CustomerUpdated = 1,
    DealCreated = 2,
    DealStageChanged = 3,
    FollowUpCreated = 4,
    FollowUpCompleted = 5,
    OrderCreated = 6,
    OrderStatusChanged = 7,
    NoteCreated = 8,
    PriceAdjustmentRequested = 9,
    PriceAdjustmentApproved = 10,
    PriceAdjustmentRejected = 11,
    SyncCompleted = 12
}
