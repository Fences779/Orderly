namespace Orderly.Contracts.Realtime;

public static class RealtimeEvent
{
    public const string EntityCreated = "EntityCreated";
    public const string EntityUpdated = "EntityUpdated";
    public const string EntityArchived = "EntityArchived";
    public const string EntityRecovered = "EntityRecovered";
    public const string InventoryChanged = "InventoryChanged";
    public const string DashboardInvalidated = "DashboardInvalidated";
    public const string PriceChangeRequestCreated = "PriceChangeRequestCreated";
    public const string PriceChangeRequestReviewed = "PriceChangeRequestReviewed";
    public const string AuditLogCreated = "AuditLogCreated";
    public const string EditingPresenceChanged = "EditingPresenceChanged";
    public const string UserOnline = "UserOnline";
    public const string UserOffline = "UserOffline";
    public const string EmergencyDraftSubmitted = "EmergencyDraftSubmitted";
}
