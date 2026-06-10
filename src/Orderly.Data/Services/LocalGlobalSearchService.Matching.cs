using Orderly.Core.Models;

namespace Orderly.Data.Services;

public sealed partial class LocalGlobalSearchService
{
    private static MatchResult EvaluateMatch(string query, params SearchField[] fields)
    {
        var best = MatchResult.None;
        foreach (var field in fields)
        {
            var score = GetFieldScore(field.Value, query, field.BaseScore);
            if (score <= 0)
            {
                continue;
            }

            var candidate = new MatchResult(true, field.Name, score);
            if (candidate.Score > best.Score || candidate.Score == best.Score && string.CompareOrdinal(candidate.FieldName, best.FieldName) < 0)
            {
                best = candidate;
            }
        }

        return best;
    }

    private static int GetFieldScore(string? value, string query, int baseScore)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var text = value.Trim();
        if (!text.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.Equals(text, query, StringComparison.OrdinalIgnoreCase))
        {
            return baseScore + 60;
        }

        if (text.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return baseScore + 30;
        }

        return baseScore + 10;
    }

    private static string NormalizeQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return string.Empty;
        }

        var normalized = query.Trim();
        if (normalized.Any(char.IsControl))
        {
            normalized = new string(normalized.Where(character => !char.IsControl(character)).ToArray()).Trim();
        }

        return normalized.Length <= MaxQueryLength
            ? normalized
            : normalized[..MaxQueryLength];
    }

    private static int ClampLimit(int requestedLimit)
    {
        return Math.Clamp(requestedLimit <= 0 ? DefaultLimit : requestedLimit, 1, MaxLimit);
    }

    private readonly record struct SearchField(string Name, string? Value, int BaseScore);

    private readonly record struct MatchResult(bool IsMatch, string FieldName, int Score)
    {
        public static MatchResult None { get; } = new(false, string.Empty, 0);
    }

    private sealed class SearchResultComparer : IComparer<SearchResultItem>
    {
        public static SearchResultComparer Instance { get; } = new();

        public int Compare(SearchResultItem? left, SearchResultItem? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return 1;
            }

            if (right is null)
            {
                return -1;
            }

            var scoreComparison = right.Score.CompareTo(left.Score);
            if (scoreComparison != 0)
            {
                return scoreComparison;
            }

            var occurredAtComparison = right.OccurredAt.CompareTo(left.OccurredAt);
            if (occurredAtComparison != 0)
            {
                return occurredAtComparison;
            }

            var typeRankComparison = GetTypeRank(left.Type).CompareTo(GetTypeRank(right.Type));
            if (typeRankComparison != 0)
            {
                return typeRankComparison;
            }

            var customerComparison = Nullable.Compare(right.CustomerId, left.CustomerId);
            if (customerComparison != 0)
            {
                return customerComparison;
            }

            var orderComparison = Nullable.Compare(right.OrderId, left.OrderId);
            if (orderComparison != 0)
            {
                return orderComparison;
            }

            var relatedIdComparison = Nullable.Compare(right.RelatedEntityId, left.RelatedEntityId);
            if (relatedIdComparison != 0)
            {
                return relatedIdComparison;
            }

            return StringComparer.Ordinal.Compare(left.Id, right.Id);
        }

        private static int GetTypeRank(SearchResultType type)
        {
            return type switch
            {
                SearchResultType.Customer => 1,
                SearchResultType.Order => 2,
                SearchResultType.ConversationMessage => 3,
                SearchResultType.AiSuggestion => 4,
                SearchResultType.OcrResult => 5,
                SearchResultType.FollowUp => 6,
                SearchResultType.ActivityLog => 7,
                _ => 99
            };
        }
    }
}
