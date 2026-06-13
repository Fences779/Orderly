namespace Orderly.Core.Commerce;

/// <summary>
/// A customer order owned by a single workspace (Req 2.2). An order carries three
/// <b>independent</b> stage dimensions — <see cref="SalesStage"/>, <see cref="PaymentStage"/>, and
/// <see cref="FulfillmentStage"/> — that evolve separately (Req 4.3): a payment change never forces
/// a sales or fulfillment change and vice versa. It also carries the monetary fields recomputed by
/// <c>IOrderService</c> on create/update (Req 4.2). Every stage and monetary mutation routes through
/// the base touch mechanism so <see cref="CommerceEntity.UpdatedAt"/> advances (Req 2.8).
/// </summary>
public sealed class Order : WorkspaceScopedEntity
{
    private string? _orderNo;
    private Guid? _customerId;
    private OrderSalesStage _salesStage = OrderSalesStage.Draft;
    private OrderPaymentStage _paymentStage = OrderPaymentStage.Unpaid;
    private OrderFulfillmentStage _fulfillmentStage = OrderFulfillmentStage.NotStarted;
    private CommerceMoney _subtotal = CommerceMoney.Zero;
    private CommerceMoney _total = CommerceMoney.Zero;
    private CommerceMoney _cost = CommerceMoney.Zero;
    private CommerceMoney _grossProfit = CommerceMoney.Zero;
    private decimal _grossMargin;
    private CommerceMoney _paidAmount = CommerceMoney.Zero;
    private CommerceMoney _receivableAmount = CommerceMoney.Zero;
    private DateTime _orderedAt = DateTime.UtcNow;
    private string? _note;

    /// <summary>Optional human-facing order number; the deterministic import match key for orders.</summary>
    public string? OrderNo
    {
        get => _orderNo;
        set { _orderNo = value; MarkUpdated(); }
    }

    /// <summary>Optional link to the placing <see cref="Customer"/>.</summary>
    public Guid? CustomerId
    {
        get => _customerId;
        set { _customerId = value; MarkUpdated(); }
    }

    /// <summary>Sales dimension stage. Independent of payment and fulfillment (Req 4.3).</summary>
    public OrderSalesStage SalesStage
    {
        get => _salesStage;
        set { _salesStage = value; MarkUpdated(); }
    }

    /// <summary>Payment dimension stage. Independent of sales and fulfillment (Req 4.3).</summary>
    public OrderPaymentStage PaymentStage
    {
        get => _paymentStage;
        set { _paymentStage = value; MarkUpdated(); }
    }

    /// <summary>Fulfillment dimension stage. Independent of sales and payment (Req 4.3).</summary>
    public OrderFulfillmentStage FulfillmentStage
    {
        get => _fulfillmentStage;
        set { _fulfillmentStage = value; MarkUpdated(); }
    }

    /// <summary>Sum of line totals before adjustments (Req 4.2). Monetary, scale 2.</summary>
    public CommerceMoney Subtotal
    {
        get => _subtotal;
        set { _subtotal = value; MarkUpdated(); }
    }

    /// <summary>Final order total (Req 4.2). Monetary, scale 2.</summary>
    public CommerceMoney Total
    {
        get => _total;
        set { _total = value; MarkUpdated(); }
    }

    /// <summary>Aggregate cost of the order (Req 4.2). Monetary, scale 2.</summary>
    public CommerceMoney Cost
    {
        get => _cost;
        set { _cost = value; MarkUpdated(); }
    }

    /// <summary>Gross profit (total − cost) (Req 4.2). Monetary, scale 2.</summary>
    public CommerceMoney GrossProfit
    {
        get => _grossProfit;
        set { _grossProfit = value; MarkUpdated(); }
    }

    /// <summary>Gross margin as a percentage in [0, 100] rounded to 2 decimal places (Req 4.2).</summary>
    public decimal GrossMargin
    {
        get => _grossMargin;
        set { _grossMargin = value; MarkUpdated(); }
    }

    /// <summary>Amount paid so far (Req 4.2). Monetary, scale 2.</summary>
    public CommerceMoney PaidAmount
    {
        get => _paidAmount;
        set { _paidAmount = value; MarkUpdated(); }
    }

    /// <summary>Outstanding receivable (total − paid) (Req 4.2). Monetary, scale 2.</summary>
    public CommerceMoney ReceivableAmount
    {
        get => _receivableAmount;
        set { _receivableAmount = value; MarkUpdated(); }
    }

    /// <summary>The UTC moment the order was placed.</summary>
    public DateTime OrderedAt
    {
        get => _orderedAt;
        set { _orderedAt = value; MarkUpdated(); }
    }

    /// <summary>Optional free-text note.</summary>
    public string? Note
    {
        get => _note;
        set { _note = value; MarkUpdated(); }
    }
}
