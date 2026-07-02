using Orderly.Core.Commerce;

namespace Orderly.Contracts.Commerce;

public sealed class CashFlowEntryCommand : WriteCommandBase
{
    public CashFlowDirection Direction { get; set; }
    public decimal Amount { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public DateTime? DueDateUtc { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public Guid? OrderId { get; set; }
    public string? BusinessKey { get; set; }
}
