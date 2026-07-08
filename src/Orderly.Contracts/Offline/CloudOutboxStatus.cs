namespace Orderly.Contracts.Offline;

public static class CloudOutboxStatus
{
    public const string Pending = "Pending";
    public const string Submitted = "Submitted";
    public const string Failed = "Failed";
}
