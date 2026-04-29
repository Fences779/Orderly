namespace Orderly.Core.Models;

public sealed class SearchRequest
{
    public string Query { get; set; } = string.Empty;
    public int Limit { get; set; } = 50;
}
