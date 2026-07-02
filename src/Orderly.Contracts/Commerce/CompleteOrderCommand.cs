namespace Orderly.Contracts.Commerce;

public sealed class CompleteOrderCommand : WriteCommandBase
{
    public DateTime CompletedAtUtc { get; set; }
}
