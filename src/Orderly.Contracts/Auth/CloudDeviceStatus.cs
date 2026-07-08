namespace Orderly.Contracts.Auth;

public static class CloudDeviceStatus
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Revoked = "Revoked";
    public const string Disabled = "Disabled";

    public static bool IsActive(string? status) =>
        string.Equals(status, Approved, StringComparison.OrdinalIgnoreCase);
}
