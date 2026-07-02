namespace Orderly.Contracts.Commerce;

public sealed class ReviewPriceChangeCommand : WriteCommandBase
{
    public string ReviewNote { get; set; } = string.Empty;
}
