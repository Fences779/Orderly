using System;

namespace Orderly.Core.Models;

public static class AutoReplyState
{
    public const string Prepared = "prepared";
    public const string Copied = "copied";
    public const string Sent = "sent";
    public const string Rejected = "rejected";

    public static bool IsPrepared(string? state)
    {
        return StateEquals(state, Prepared);
    }

    public static bool IsCopied(string? state)
    {
        return StateEquals(state, Copied);
    }

    public static bool IsSent(string? state)
    {
        return StateEquals(state, Sent);
    }

    public static bool IsRejected(string? state)
    {
        return StateEquals(state, Rejected);
    }

    public static bool IsPreparedDraft(string? state)
    {
        return IsPrepared(state) || IsCopied(state);
    }

    private static bool StateEquals(string? state, string expected)
    {
        return string.Equals(state, expected, StringComparison.OrdinalIgnoreCase);
    }
}
