namespace Orderly.Contracts.Commerce;

public abstract class WriteCommandBase
{
    public string ClientRequestId { get; set; } = Guid.NewGuid().ToString("N");
    public long ExpectedRevision { get; set; }
    public string IdempotencyKey
    {
        get => ClientRequestId;
        set => ClientRequestId = value;
    }
    public long BaseVersion
    {
        get => ExpectedRevision;
        set => ExpectedRevision = value;
    }
    public List<string> ChangedFields { get; set; } = new();
    public string? Reason { get; set; }
}
