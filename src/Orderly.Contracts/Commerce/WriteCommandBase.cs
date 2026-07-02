namespace Orderly.Contracts.Commerce;

public abstract class WriteCommandBase
{
    public string ClientRequestId { get; set; } = Guid.NewGuid().ToString("N");
    public long ExpectedRevision { get; set; }
    public string? Reason { get; set; }
}
