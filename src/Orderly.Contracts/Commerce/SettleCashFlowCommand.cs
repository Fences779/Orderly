namespace Orderly.Contracts.Commerce;

public sealed class SettleCashFlowCommand : WriteCommandBase
{
    public decimal Amount { get; set; }
    public DateTime AsOfUtc { get; set; }
}
