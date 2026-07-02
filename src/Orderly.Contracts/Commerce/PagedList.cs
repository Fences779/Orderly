namespace Orderly.Contracts.Commerce;

public sealed class PagedList<T>
{
    public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public long TotalCount { get; set; }
    public string? Sort { get; set; }
    public string? FilterSummary { get; set; }
    public long LatestSequence { get; set; }
}
