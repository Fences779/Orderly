namespace Orderly.Contracts.Commerce;

public sealed class PermanentDeleteRequest
{
    public bool Confirm { get; set; }
    public string? Reason { get; set; }
    public string? ClientRequestId { get; set; }
}
