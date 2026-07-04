using Orderly.Core.Commerce;

namespace Orderly.Contracts.Commerce;

public sealed class UpdateCashFlowEntryCommand : WriteCommandBase
{
    public Guid CashFlowEntryId { get; set; }
    public CashFlowDirection? Direction { get; set; }
    public decimal? Amount { get; set; }
    public DateTime? OccurredAtUtc { get; set; }
    public DateTime? DueDateUtc { get; set; }
    public string? CategoryName { get; set; }
    public Guid? OrderId { get; set; }
    public string? BusinessKey { get; set; }
}
