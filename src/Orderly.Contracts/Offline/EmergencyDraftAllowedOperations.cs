namespace Orderly.Contracts.Offline;

public static class EmergencyDraftAllowedOperations
{
    public const string OrderNote = "order/note";
    public const string OrderStage = "order/stage";
    public const string CustomerNote = "customer/note";
    public const string BusinessTaskStatus = "businessTask/status";

    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        OrderNote,
        OrderStage,
        CustomerNote,
        BusinessTaskStatus
    };

    public static bool IsAllowed(string entityType, string operationType)
    {
        return Allowed.Contains($"{entityType}/{operationType}");
    }
}
