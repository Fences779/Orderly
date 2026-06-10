namespace Orderly.Core.Security;

public static class MasterPasswordPolicy
{
    public const int MinimumLength = 12;
    public const int MaximumLength = 128;
    public const string ValidationMessage = "主密码必须为 12-128 位，且同时包含大写字母、小写字母、数字和特殊字符，不能包含空白字符，也不能有前后空格。";

    public static bool TryValidate(string? password, out string errorMessage)
    {
        if (string.IsNullOrEmpty(password))
        {
            errorMessage = "主密码不能为空。";
            return false;
        }

        if (password.Length < MinimumLength
            || password.Length > MaximumLength
            || password.Any(char.IsWhiteSpace)
            || !password.Any(char.IsUpper)
            || !password.Any(char.IsLower)
            || !password.Any(char.IsDigit)
            || !password.Any(ch => !char.IsLetterOrDigit(ch)))
        {
            errorMessage = ValidationMessage;
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }
}
