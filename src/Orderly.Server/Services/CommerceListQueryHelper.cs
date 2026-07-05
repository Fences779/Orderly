using System.Text;
using System.Text.RegularExpressions;

namespace Orderly.Server.Services;

/// <summary>
/// Builds safe ORDER BY fragments for commerce list endpoints.
/// Only whitelisted fields and directions are accepted; arbitrary SQL is rejected.
/// </summary>
public static class CommerceListQueryHelper
{
    private static readonly Regex SortTokenRegex = new(
        @"^(?<field>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*(?<dir>asc|desc)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Parses a sort expression such as "orderedAt:desc,createdAt:asc" and returns a safe
    /// ORDER BY clause using only whitelisted column names. Falls back to <paramref name="defaultOrderBy"/>.
    /// </summary>
    public static string BuildOrderBy(string? sort, string defaultOrderBy, IReadOnlyDictionary<string, string> fieldWhitelist)
    {
        if (string.IsNullOrWhiteSpace(sort))
        {
            return defaultOrderBy;
        }

        var segments = sort.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var orderBy = new StringBuilder();

        foreach (var segment in segments)
        {
            var match = SortTokenRegex.Match(segment);
            if (!match.Success)
            {
                continue;
            }

            var field = match.Groups["field"].Value;
            var dir = match.Groups["dir"].Value.ToUpperInvariant();

            if (!fieldWhitelist.TryGetValue(field, out var columnName))
            {
                continue;
            }

            if (orderBy.Length > 0)
            {
                orderBy.Append(", ");
            }

            orderBy.Append(columnName);
            orderBy.Append(' ');
            orderBy.Append(dir);
        }

        return orderBy.Length > 0 ? orderBy.ToString() : defaultOrderBy;
    }

    /// <summary>
    /// Returns a user-readable summary of the applied sort (e.g. "orderedAt desc").
    /// </summary>
    public static string? BuildSortSummary(string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
        {
            return null;
        }

        var segments = sort.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(", ", segments.Select(s => s.Replace(":", " ", StringComparison.OrdinalIgnoreCase)));
    }
}
