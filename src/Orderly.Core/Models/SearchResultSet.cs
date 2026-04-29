namespace Orderly.Core.Models;

public sealed class SearchResultSet
{
    public string Query { get; set; } = string.Empty;
    public int Limit { get; set; }
    public int TotalCount { get; set; }
    public IReadOnlyList<SearchResultItem> Items { get; set; } = [];
}
