namespace Orderly.Contracts.Commerce;

public sealed class PriceChangeRequestCommand : WriteCommandBase
{
    public Guid ProductId { get; set; }
    public decimal ProposedPrice { get; set; }
    public string? ChangeReason { get; set; }
}
