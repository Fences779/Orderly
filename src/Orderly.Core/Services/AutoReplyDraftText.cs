namespace Orderly.Core.Services;

public static class AutoReplyDraftText
{
    public const string Prefix = "【本地草稿 / 未发送】";

    public static string EnsurePrefix(string suggestionText)
    {
        if (string.IsNullOrWhiteSpace(suggestionText))
        {
            return Prefix;
        }

        return suggestionText.StartsWith(Prefix, StringComparison.Ordinal)
            ? suggestionText
            : $"{Prefix}{suggestionText}";
    }

    public static string StripPrefix(string suggestionText)
    {
        if (string.IsNullOrWhiteSpace(suggestionText))
        {
            return string.Empty;
        }

        return suggestionText.StartsWith(Prefix, StringComparison.Ordinal)
            ? suggestionText[Prefix.Length..].TrimStart()
            : suggestionText;
    }
}
