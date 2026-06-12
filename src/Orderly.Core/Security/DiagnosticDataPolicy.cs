namespace Orderly.Core.Security;

public static class DiagnosticDataPolicy
{
    public static string? ClassifyError(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            return null;
        }

        var value = errorMessage.Trim();

        if (ContainsAny(value, "timeout", "timed out", "超时"))
        {
            return "timeout";
        }

        if (ContainsAny(value, "unauthorized", "unauthenticated", "401", "未授权"))
        {
            return "unauthorized";
        }

        if (ContainsAny(value, "forbidden", "permission denied", "403", "权限", "拒绝"))
        {
            return "forbidden";
        }

        if (ContainsAny(value, "not found", "404", "未找到", "不存在"))
        {
            return "not_found";
        }

        if (ContainsAny(value, "conflict", "409", "冲突"))
        {
            return "conflict";
        }

        if (ContainsAny(value, "too many requests", "rate limit", "429", "限流"))
        {
            return "rate_limited";
        }

        if (ContainsAny(value, "tls", "ssl", "certificate", "证书"))
        {
            return "tls_error";
        }

        if (ContainsAny(value, "network", "connection", "socket", "dns", "网络", "连接"))
        {
            return "network_error";
        }

        if (ContainsAny(value, "decrypt", "encrypt", "cryptographic", "解密", "加密"))
        {
            return "cryptographic_error";
        }

        if (ContainsAny(value, "database", "sqlite", "sql", "数据库"))
        {
            return "database_error";
        }

        if (ContainsAny(value, "file", "directory", "path", "文件", "目录", "路径"))
        {
            return "io_error";
        }

        if (ContainsAny(value, "canceled", "cancelled", "cancel", "取消"))
        {
            return "canceled";
        }

        return "redacted_error";
    }

    private static bool ContainsAny(string value, params string[] candidates)
    {
        return candidates.Any(candidate =>
            value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }
}
