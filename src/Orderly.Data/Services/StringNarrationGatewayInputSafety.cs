namespace Orderly.Data.Services;

internal static class StringNarrationGatewayInputSafety
{
    public const int MaxIdentifierCharacters = 128;
    public const int MaxFilterCharacters = 64;
    public const int MaxKeywordCharacters = 128;
    public const int MaxRemarkCharacters = 512;
    public const int MaxPageSize = 200;

    public static int NormalizePage(int page)
    {
        return page <= 0 ? 1 : Math.Min(page, 10_000);
    }

    public static int NormalizePageSize(int pageSize, int fallback)
    {
        return pageSize <= 0 ? fallback : Math.Clamp(pageSize, 1, MaxPageSize);
    }

    public static long NormalizeTimestamp(long timestamp)
    {
        return timestamp <= 0 ? 0 : timestamp;
    }

    public static string NormalizeIdentifier(string? value, string fieldName)
    {
        return NormalizeText(value, fieldName, MaxIdentifierCharacters);
    }

    public static string NormalizeFilter(string? value, string fieldName)
    {
        return NormalizeText(value, fieldName, MaxFilterCharacters);
    }

    public static string NormalizeKeyword(string? value, string fieldName)
    {
        return NormalizeText(value, fieldName, MaxKeywordCharacters);
    }

    public static string NormalizeRemark(string? value, string fieldName)
    {
        return NormalizeText(value, fieldName, MaxRemarkCharacters);
    }

    private static string NormalizeText(string? value, string fieldName, int maxCharacters)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = ReplaceAllowedWhitespace(value.Trim());
        if (normalized.Length > maxCharacters)
        {
            throw new InvalidOperationException($"{fieldName} 长度超过上限 {maxCharacters}。");
        }

        return normalized;
    }

    private static string ReplaceAllowedWhitespace(string value)
    {
        var chars = new char[value.Length];
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            if (!char.IsControl(ch))
            {
                chars[index] = ch;
                continue;
            }

            chars[index] = ch is '\r' or '\n' or '\t'
                ? ' '
                : throw new InvalidOperationException("网关请求参数包含不允许的控制字符。");
        }

        return new string(chars).Trim();
    }
}
