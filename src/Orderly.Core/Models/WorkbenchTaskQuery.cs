namespace Orderly.Core.Models;

public sealed class WorkbenchTaskQuery
{
    public WorkbenchTaskFilter Filter { get; set; } = new();
    public int? Limit { get; set; }
}
