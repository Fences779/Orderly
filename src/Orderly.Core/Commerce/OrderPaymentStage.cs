namespace Orderly.Core.Commerce;

/// <summary>
/// The payment dimension of an order's three independent stage dimensions (Req 4.3).
/// Evolves independently of sales and fulfillment.
/// </summary>
public enum OrderPaymentStage
{
    Unpaid = 0,
    PartiallyPaid = 1,
    Paid = 2,
    Refunded = 3
}
