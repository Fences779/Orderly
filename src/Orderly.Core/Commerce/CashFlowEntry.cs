namespace Orderly.Core.Commerce;

/// <summary>
/// A cash-flow entry owned by a single workspace (Req 2.2). The <see cref="Direction"/> is one of
/// income, expense, or transfer; receivable and payable are represented via
/// <see cref="SettlementStatus"/> and <see cref="DueDate"/> rather than as directions (Req 4.12).
/// The <see cref="ImportBatchId"/> + <see cref="SourceRowKey"/> pair forms the deterministic import
/// match key, and the optional <see cref="BusinessKey"/> makes service-generated entries idempotent
/// (Req 4.20, 18.6). Mutable fields advance <see cref="CommerceEntity.UpdatedAt"/> when changed (Req 2.8).
/// </summary>
public sealed class CashFlowEntry : WorkspaceScopedEntity
{
    private CashFlowDirection _direction = CashFlowDirection.Income;
    private CommerceMoney _amount = CommerceMoney.Zero;
    private CommerceMoney _settledAmount = CommerceMoney.Zero;
    private CashFlowSettlementStatus _settlementStatus = CashFlowSettlementStatus.Settled;
    private DateTime _occurredAt = DateTime.UtcNow;
    private DateTime? _dueDate;
    private string? _categoryName;
    private Guid? _orderId;
    private Guid? _paymentRecordId;

    /// <summary>Income, expense, or transfer (Req 4.12).</summary>
    public CashFlowDirection Direction
    {
        get => _direction;
        set { _direction = value; MarkUpdated(); }
    }

    /// <summary>The entry amount. Monetary, scale 2.</summary>
    public CommerceMoney Amount
    {
        get => _amount;
        set { _amount = value; MarkUpdated(); }
    }

    /// <summary>Amount settled so far for receivable/payable entries. Monetary, scale 2.</summary>
    public CommerceMoney SettledAmount
    {
        get => _settledAmount;
        set { _settledAmount = value; MarkUpdated(); }
    }

    /// <summary>Settlement state; encodes receivable/payable status (Req 4.12).</summary>
    public CashFlowSettlementStatus SettlementStatus
    {
        get => _settlementStatus;
        set { _settlementStatus = value; MarkUpdated(); }
    }

    /// <summary>The UTC moment the cash-flow event occurred.</summary>
    public DateTime OccurredAt
    {
        get => _occurredAt;
        set { _occurredAt = value; MarkUpdated(); }
    }

    /// <summary>Optional due date for a receivable/payable entry (Req 4.12).</summary>
    public DateTime? DueDate
    {
        get => _dueDate;
        set { _dueDate = value; MarkUpdated(); }
    }

    /// <summary>Optional category label (for example an income or expense category).</summary>
    public string? CategoryName
    {
        get => _categoryName;
        set { _categoryName = value; MarkUpdated(); }
    }

    /// <summary>Optional link to the originating <see cref="Order"/>.</summary>
    public Guid? OrderId
    {
        get => _orderId;
        set { _orderId = value; MarkUpdated(); }
    }

    /// <summary>Optional link back to the generating <see cref="PaymentRecord"/>.</summary>
    public Guid? PaymentRecordId
    {
        get => _paymentRecordId;
        set { _paymentRecordId = value; MarkUpdated(); }
    }

    /// <summary>Import batch identifier; part of the deterministic import match key. Fixed at creation.</summary>
    public string? ImportBatchId { get; init; }

    /// <summary>Stable per-row import key; part of the deterministic import match key. Fixed at creation.</summary>
    public string? SourceRowKey { get; init; }

    /// <summary>Stable business key used for idempotent generation by the service layer (Req 4.20, 18.6).</summary>
    public string? BusinessKey { get; init; }
}
