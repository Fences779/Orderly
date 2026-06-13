namespace Orderly.Core.Commerce;

/// <summary>
/// A payment recorded against an order, owned by a single workspace (Req 2.2). Each payment record
/// links to <b>at most one</b> <see cref="CashFlowEntry"/> via <see cref="CashFlowEntryId"/>
/// (Req 4.18). The optional <see cref="BusinessKey"/> makes service-generated payments idempotent
/// so re-running completion/payment produces no duplicates (Req 4.20, 18.6). Mutable fields advance
/// <see cref="CommerceEntity.UpdatedAt"/> when changed (Req 2.8).
/// </summary>
public sealed class PaymentRecord : WorkspaceScopedEntity
{
    private Guid? _orderId;
    private Guid? _cashFlowEntryId;
    private CommerceMoney _amount = CommerceMoney.Zero;
    private DateTime _paidAt = DateTime.UtcNow;
    private string? _method;

    /// <summary>Optional link to the settled <see cref="Order"/>.</summary>
    public Guid? OrderId
    {
        get => _orderId;
        set { _orderId = value; MarkUpdated(); }
    }

    /// <summary>
    /// Link to the single <see cref="CashFlowEntry"/> generated for this payment, or null if none.
    /// At most one cash-flow entry is ever linked (Req 4.18).
    /// </summary>
    public Guid? CashFlowEntryId
    {
        get => _cashFlowEntryId;
        set { _cashFlowEntryId = value; MarkUpdated(); }
    }

    /// <summary>The payment amount. Monetary, scale 2.</summary>
    public CommerceMoney Amount
    {
        get => _amount;
        set { _amount = value; MarkUpdated(); }
    }

    /// <summary>The UTC moment the payment was made.</summary>
    public DateTime PaidAt
    {
        get => _paidAt;
        set { _paidAt = value; MarkUpdated(); }
    }

    /// <summary>Optional payment method label.</summary>
    public string? Method
    {
        get => _method;
        set { _method = value; MarkUpdated(); }
    }

    /// <summary>Stable business key used for idempotent generation by the service layer (Req 4.20, 18.6).</summary>
    public string? BusinessKey { get; init; }
}
