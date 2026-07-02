using Orderly.Core.Commerce;

namespace Orderly.Contracts.Commerce;

public sealed class CloudCashFlowEntryDto : CloudEntityDto
{
    public Guid WorkspaceId { get; set; }
    public CashFlowDirection Direction { get; set; }
    public decimal Amount { get; set; }
    public decimal SettledAmount { get; set; }
    public CashFlowSettlementStatus SettlementStatus { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public DateTime? DueDateUtc { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public Guid? OrderId { get; set; }
    public Guid? PaymentRecordId { get; set; }
    public string? BusinessKey { get; set; }
}
