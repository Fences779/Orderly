namespace Orderly.Contracts.Permissions;

public static class CloudRole
{
    public const string Admin = "Admin";
    public const string Employee = "Employee";

    public static bool IsValid(string? role) =>
        string.Equals(role, Admin, StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, Employee, StringComparison.OrdinalIgnoreCase);
}
