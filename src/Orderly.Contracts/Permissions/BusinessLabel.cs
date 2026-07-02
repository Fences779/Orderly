namespace Orderly.Contracts.Permissions;

public static class BusinessLabel
{
    public const string Operator = "运营负责人";
    public const string Investor = "投资方";
    public const string Staff = "员工";

    public static bool IsValid(string? label) =>
        string.Equals(label, Operator, StringComparison.Ordinal)
        || string.Equals(label, Investor, StringComparison.Ordinal)
        || string.Equals(label, Staff, StringComparison.Ordinal);
}
